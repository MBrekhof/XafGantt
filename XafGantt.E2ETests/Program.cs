using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Playwright;

// E2E phase gate for XafGantt: the Veto Gantt chart as an XAF Blazor list editor.
// Run with:  dotnet run --project XafGantt.E2ETests
// Re-seeds the tasks, starts the app on :5110, asserts, stops the app. Exit 0 = passed.

const string BaseUrl = "http://localhost:5110";
const string GanttUrl = BaseUrl + "/ProjectTask_ListView_Gantt";
const string ConnectionString =
    @"Data Source=(localdb)\mssqllocaldb;Integrated Security=SSPI;Initial Catalog=XafGantt;Encrypt=False";

var repoRoot = FindRepoRoot();
var blazorProj = Path.Combine(repoRoot, "XafGantt.Blazor.Server");
var screenshotDir = Path.Combine(AppContext.BaseDirectory, "screenshots");
Directory.CreateDirectory(screenshotDir);
Console.WriteLine($"Screenshots: {screenshotDir}");

Process? app = null;
IPage? page = null;
IPlaywright? playwright = null;
IBrowser? browser = null;
var appOutput = new System.Text.StringBuilder();
var browserErrors = new List<string>();
var failed = false;

try
{
    Step("Port 5110 must be free");
    Assert(!await IsUp(), "nothing answers on " + BaseUrl);

    Step("Build Blazor.Server");
    RunOrThrow("dotnet", $"build \"{blazorProj}\" -v q --nologo");

    Step("Re-seed: drop the tasks and run the app's own database updater");
    // The user model differences go too: they hold the tabs left open by the previous run.
    try { Sql("DELETE FROM ProjectTaskDependency; UPDATE ProjectTasks SET ParentID = NULL; DELETE FROM ProjectTasks; DELETE FROM ModelDifferenceAspects; DELETE FROM ModelDifferences;"); }
    catch (SqlException ex) when (ex.Number is 4060 or 208) { Console.WriteLine("    (no database yet)"); }
    // An XAF template app only creates/updates its database under a debugger; this is its CLI path.
    RunOrThrow("dotnet", $"run --no-build --no-launch-profile --project \"{blazorProj}\" -- --updateDatabase --silent --forceUpdate");
    Assert(int.Parse(SqlScalar("SELECT COUNT(*) FROM ProjectTasks")!) == 16, "16 seeded tasks");
    // Bad data the chart must survive: a task saved without dates (VetoGantt throws on year 1)
    // and two tasks that are each other's parent (Veto drops rows without a root).
    Sql(@"DECLARE @a uniqueidentifier = NEWID(), @b uniqueidentifier = NEWID(), @d date = (SELECT MIN(StartDate) FROM ProjectTasks);
          INSERT ProjectTasks (ID, Name, StartDate, EndDate, Progress, IsMilestone, ParentID, GCRecord, OptimisticLockField) VALUES
            (NEWID(), 'Unscheduled', '0001-01-01', '0001-01-01', 0, 0, NULL, 0, 0),
            (@a, 'Cycle A', @d, DATEADD(day, 2, @d), 0, 0, NULL, 0, 0),
            (@b, 'Cycle B', @d, DATEADD(day, 2, @d), 0, 0, @a, 0, 0);
          UPDATE ProjectTasks SET ParentID = @b WHERE ID = @a;");

    Step("Start the app on :5110");
    app = StartApp(blazorProj, appOutput);
    await WaitForHttpOk(appOutput);

    playwright = await Playwright.CreateAsync();
    browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });

    // ---------- Admin ----------
    page = await NewPage(browser, browserErrors);
    Step("Log in as Admin and open Tasks (Gantt)");
    await Login(page, "Admin");
    await OpenGantt(page);
    var bars = page.Locator("[data-testid^='gantt-bar-']");
    Assert(await bars.CountAsync() >= 16, $"at least 16 bars rendered (found {await bars.CountAsync()})");
    await Shot(page, "01-gantt-light");

    Step("Bad data: an undated task is left off the chart, a parent cycle is still drawn");
    Assert(await Bar(page, "Unscheduled").CountAsync() == 0, "'Unscheduled' (dates in year 1) is not on the chart");
    Assert(await Bar(page, "Cycle A").CountAsync() == 1 && await Bar(page, "Cycle B").CountAsync() == 1, "'Cycle A' and 'Cycle B' are both drawn");

    Step("Summary rows and milestones are not draggable; leaf tasks are");
    Assert(await Bar(page, "Front end").GetAttributeAsync("data-editable") == "true", "leaf 'Front end' is editable");
    Assert(await Bar(page, "Build").GetAttributeAsync("data-editable") is null, "parent 'Build' is not editable");
    Assert(await Bar(page, "Go live").GetAttributeAsync("data-editable") is null, "milestone 'Go live' is not editable");

    Step("A parent bar spans its children (display roll-up)");
    var buildBox = await BoxOf(page, "Build");
    var frontBox = await BoxOf(page, "Front end");
    var integrationBox = await BoxOf(page, "Integration");
    Assert(Math.Abs(buildBox.X - frontBox.X) < 2, "'Build' starts where its first child 'Front end' starts");
    Assert(Math.Abs((buildBox.X + buildBox.Width) - (integrationBox.X + integrationBox.Width)) < 2, "'Build' ends where its last child 'Integration' ends");

    Step("Keyboard move: ArrowRight on 'Front end' (Wed -> Thu) is committed");
    var (start0, end0) = Dates("Front end");
    var left0 = await LeftOf(page, "Front end");
    var frontDayWidth = (await BoxOf(page, "Front end")).Width / ((end0 - start0).Days + 1);
    await Bar(page, "Front end").FocusAsync();
    await page.Keyboard.PressAsync("ArrowRight");
    await WaitForDates("Front end", start0.AddDays(1), end0.AddDays(1));
    await PollAsync(async () => Math.Abs(await LeftOf(page, "Front end") - (left0 + frontDayWidth)) < 2,
        "the bar stays one day to the right after the commit (no snap back)");

    Step("Second move accumulates (Thu -> Fri)");
    await Bar(page, "Front end").FocusAsync();
    await page.Keyboard.PressAsync("ArrowRight");
    await WaitForDates("Front end", start0.AddDays(2), end0.AddDays(2));
    await page.WaitForTimeoutAsync(500);
    var fridayLeft = await LeftOf(page, "Front end");

    Step("Third move (Fri -> Sat) breaks the workday rule: rejected, message shown, bar returns");
    await Bar(page, "Front end").FocusAsync();
    await page.Keyboard.PressAsync("ArrowRight");
    await page.GetByText("cannot start in a weekend").First.WaitForAsync(new() { Timeout = 10_000 });
    Console.WriteLine("    [ok] validation message shown");
    await page.WaitForTimeoutAsync(1000);
    var (startR, endR) = Dates("Front end");
    Assert(startR == start0.AddDays(2) && endR == end0.AddDays(2), "stored dates unchanged after the rejected move");
    var rejectedLeft = await LeftOf(page, "Front end");
    Assert(Math.Abs(rejectedLeft - fridayLeft) < 2, $"bar is back at its stored position (left {rejectedLeft:0} vs {fridayLeft:0})");
    await Shot(page, "02-rejected-move");
    await DismissMessages(page);

    Step("Pointer drag: resize the end of 'Content migration' by two days");
    await page.Locator("[data-testid='gantt'][data-draggable='true']").WaitForAsync(new() { Timeout = 15_000 });
    var (cStart, cEnd) = Dates("Content migration");
    var cBox = await BoxOf(page, "Content migration");
    var dayWidth = cBox.Width / ((cEnd - cStart).Days + 1);
    var grabX = cBox.X + cBox.Width - 3;
    var grabY = cBox.Y + cBox.Height / 2;
    await page.Mouse.MoveAsync(grabX, grabY);
    await page.Mouse.DownAsync();
    await page.Mouse.MoveAsync(grabX + dayWidth, grabY, new() { Steps = 5 });
    await page.Mouse.MoveAsync(grabX + 2 * dayWidth, grabY, new() { Steps = 5 });
    await page.Mouse.UpAsync();
    await WaitForDates("Content migration", cStart, cEnd.AddDays(2));

    Step("Pointer drag: move 'Installation' one week later");
    var (iStart, iEnd) = Dates("Installation");
    var iBox = await BoxOf(page, "Installation");
    var midX = iBox.X + iBox.Width / 2;
    var midY = iBox.Y + iBox.Height / 2;
    await page.Mouse.MoveAsync(midX, midY);
    await page.Mouse.DownAsync();
    await page.Mouse.MoveAsync(midX + 3 * dayWidth, midY, new() { Steps = 5 });
    await page.Mouse.MoveAsync(midX + 7 * dayWidth, midY, new() { Steps = 5 });
    await page.Mouse.UpAsync();
    await WaitForDates("Installation", iStart.AddDays(7), iEnd.AddDays(7));
    await Shot(page, "03-after-drags");

    Step("A change made outside the chart shows after XAF Refresh (no stale override on a dragged bar)");
    var beforeRefresh = await LeftOf(page, "Installation");
    Sql($"UPDATE ProjectTasks SET StartDate = DATEADD(day, -7, StartDate), EndDate = DATEADD(day, -7, EndDate) WHERE Name = 'Installation'");
    await page.Locator("[data-action-name='Refresh']").First.ClickAsync();
    await page.WaitForTimeoutAsync(1500);
    var afterRefresh = await LeftOf(page, "Installation");
    Assert(Math.Abs((beforeRefresh - afterRefresh) - 7 * dayWidth) < dayWidth / 2,
        $"'Installation' moved back seven days in the timeline (dx {beforeRefresh - afterRefresh:0}, day {dayWidth:0})");

    Step("Context menu 'Open' shows the Detail View of the right-clicked task");
    await Bar(page, "Validation").ClickAsync(new() { Button = MouseButton.Right });
    await page.Locator("[data-testid='gantt-open']").ClickAsync();
    await page.WaitForURLAsync(url => url.Contains("ProjectTask_DetailView"), new() { Timeout = 15_000 });
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    Assert(page.Url.Contains(IdOf("Validation"), StringComparison.OrdinalIgnoreCase), "the Detail View is that of 'Validation' (key in the URL)");
    await PollAsync(async () => (await page.Locator("input").EvaluateAllAsync<string[]>("els => els.map(e => e.value)")).Contains("Validation"),
        "Detail View shows the name 'Validation'");
    await Shot(page, "04-detail-view");

    Step("Click selects a bar; the XAF Delete action then deletes that task");
    await OpenGantt(page);
    var delete = page.Locator("[data-action-name='Delete']").First;
    ILocator SelectedRow(string taskName) => page.Locator($"[data-testid='gantt-row-{IdOf(taskName)}'].veto-gantt__row--selected");

    await Bar(page, "Lab instrument rollout").ClickAsync();
    await PollAsync(async () => await SelectedRow("Lab instrument rollout").CountAsync() == 1 && !await delete.IsDisabledAsync(),
        "mouse click on a summary bar selects it and enables XAF Delete");

    // Veto finding V1: with Veto.Gantt 1.0.7 this click did nothing. Fixed in the fork build.
    var (tStart, tEnd) = Dates("Training");
    var tWidth = (await BoxOf(page, "Training")).Width;
    await Bar(page, "Training").ClickAsync();
    await PollAsync(async () => await SelectedRow("Training").CountAsync() == 1, "mouse click on a draggable bar selects it");
    Assert(Dates("Training") == (tStart, tEnd), "the click did not move the task");
    // Veto finding V8: with 1.0.7 a click collapsed the bar to the width of its label.
    Assert(Math.Abs((await BoxOf(page, "Training")).Width - tWidth) < 1, "the clicked bar kept its width");

    await Bar(page, "Validation").FocusAsync();
    await page.Keyboard.PressAsync("Enter");
    await PollAsync(async () => await SelectedRow("Validation").CountAsync() == 1, "Enter on a focused draggable bar selects it");
    await Bar(page, "Training").ClickAsync();
    await PollAsync(async () => await SelectedRow("Training").CountAsync() == 1, "clicking another bar moves the selection");
    await delete.ClickAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Yes", Exact = true }).ClickAsync();
    await Poll(() => SqlScalar("SELECT COUNT(*) FROM ProjectTasks WHERE Name = 'Training' AND GCRecord = 0") == "0",
        "'Training' is deleted in the database");
    await PollAsync(async () => await Bar(page, "Training", required: false).CountAsync() == 0, "'Training' bar is gone from the chart");

    Step("Dark theme: switch XAF to Fluent dark; the chart follows");
    await SetDarkTheme(page);
    await OpenGantt(page);
    Assert(await page.Locator("[data-testid='gantt']").GetAttributeAsync("data-veto-theme") == "dark", "chart root has data-veto-theme=dark");
    await Shot(page, "05-gantt-dark");

    // ---------- Viewer (read-only) ----------
    Step("Log in as Viewer: chart is read-only");
    var viewer = await NewPage(browser, browserErrors);
    await Login(viewer, "Viewer");
    await OpenGantt(viewer);
    Assert(await viewer.Locator("[data-testid^='gantt-bar-']").CountAsync() >= 15, "Viewer sees the bars");
    Assert(await viewer.Locator("[data-testid^='gantt-bar-'][data-editable='true']").CountAsync() == 0, "no bar is editable for Viewer");
    var (vStart, vEnd) = Dates("Back end");
    await Bar(viewer, "Back end").FocusAsync();
    await viewer.Keyboard.PressAsync("ArrowRight");
    await viewer.WaitForTimeoutAsync(1500);
    Assert(Dates("Back end") == (vStart, vEnd), "ArrowRight as Viewer changes nothing in the database");
    await Shot(viewer, "06-viewer");

    Step("No browser errors or failed asset requests");
    foreach (var e in browserErrors) Console.WriteLine("    " + e);
    Assert(browserErrors.Count == 0, "browser console/page/request errors: " + browserErrors.Count);

    Console.WriteLine("\n=== E2E PASSED ===");
}
catch (Exception ex)
{
    failed = true;
    Console.WriteLine($"\n=== E2E FAILED ===\n{ex}");
    Console.WriteLine("\n--- browser errors ---\n" + string.Join('\n', browserErrors));
    Console.WriteLine("\n--- app stdout/stderr (last 40 lines) ---");
    lock (appOutput) Console.WriteLine(string.Join('\n', appOutput.ToString().Split('\n').TakeLast(40)));
    if (page is not null)
    {
        Console.WriteLine($"\n--- page URL at failure: {page.Url}");
        try { await Shot(page, "failure"); } catch { /* best effort */ }
    }
}
finally
{
    Step("Stop app, free port 5110");
    if (browser is not null) { try { await browser.CloseAsync(); } catch { /* best effort */ } }
    playwright?.Dispose();
    KillApp(ref app);
}
return failed ? 1 : 0;

// ---------- helpers ----------

static void Step(string name) => Console.WriteLine($"\n--- {name}");

static void Assert(bool condition, string what)
{
    if (!condition) throw new Exception($"Assert failed: {what}");
    Console.WriteLine($"    [ok] {what}");
}

static Task Poll(Func<bool> condition, string what) => PollAsync(() => Task.FromResult(condition()), what);

static async Task PollAsync(Func<Task<bool>> condition, string what)
{
    for (var i = 0; i < 40; i++)
    {
        if (await condition()) { Console.WriteLine($"    [ok] {what}"); return; }
        await Task.Delay(250);
    }
    throw new Exception($"Assert failed (after 10 s): {what}");
}

async Task Shot(IPage p, string name) =>
    await p.ScreenshotAsync(new() { Path = Path.Combine(screenshotDir, name + ".png"), Animations = ScreenshotAnimations.Disabled });

static string IdOf(string taskName) =>
    SqlScalar($"SELECT ID FROM ProjectTasks WHERE Name = '{taskName}'")?.ToLowerInvariant()
    ?? throw new Exception($"task '{taskName}' not found");

static ILocator Bar(IPage p, string taskName, bool required = true)
{
    var id = required ? IdOf(taskName) : SqlScalar($"SELECT TOP 1 ID FROM ProjectTasks WHERE Name = '{taskName}'")?.ToLowerInvariant() ?? "none";
    return p.Locator($"[data-testid='gantt-bar-{id}']");
}

static async Task<LocatorBoundingBoxResult> BoxOf(IPage p, string taskName)
{
    var bar = Bar(p, taskName);
    await bar.ScrollIntoViewIfNeededAsync();
    return await bar.BoundingBoxAsync() ?? throw new Exception($"bar '{taskName}' has no box");
}

// Position inside the timeline, independent of how far the timeline is scrolled.
static async Task<double> LeftOf(IPage p, string taskName) =>
    await Bar(p, taskName).EvaluateAsync<double>("el => el.getBoundingClientRect().left - el.parentElement.getBoundingClientRect().left");

static (DateTime Start, DateTime End) Dates(string taskName)
{
    using var conn = new SqlConnection(ConnectionString);
    conn.Open();
    using var cmd = new SqlCommand("SELECT StartDate, EndDate FROM ProjectTasks WHERE Name = @n", conn);
    cmd.Parameters.AddWithValue("@n", taskName);
    using var reader = cmd.ExecuteReader();
    if (!reader.Read()) throw new Exception($"task '{taskName}' not found");
    return (reader.GetDateTime(0), reader.GetDateTime(1));
}

static async Task WaitForDates(string taskName, DateTime start, DateTime end) =>
    await Poll(() => Dates(taskName) == (start, end), $"'{taskName}' stored as {start:ddd d MMM} - {end:ddd d MMM}");

static async Task Login(IPage page, string user)
{
    // Blazor Server circuit-connect race: wait for NetworkIdle and verify the value bound.
    await page.GotoAsync($"{BaseUrl}/LoginPage", new() { WaitUntil = WaitUntilState.NetworkIdle });
    var userField = page.Locator("input[type='text'], input[name*='sername']").First;
    await userField.WaitForAsync(new() { Timeout = 20_000 });
    // A fill that lands before the circuit is attached is wiped by the first render (seen on the
    // first login after a rebuild), so fill-and-submit is retried until the app leaves the page.
    for (var attempt = 0; ; attempt++)
    {
        await userField.FillAsync(user);
        await page.GetByRole(AriaRole.Button, new() { Name = "Log In" }).ClickAsync();
        try
        {
            await page.WaitForURLAsync(url => !url.Contains("LoginPage", StringComparison.OrdinalIgnoreCase), new() { Timeout = 5_000 });
            break;
        }
        catch (TimeoutException) when (attempt < 4) { /* retry */ }
    }
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 20_000 });
}

static async Task OpenGantt(IPage page)
{
    await page.GotoAsync(GanttUrl, new() { WaitUntil = WaitUntilState.NetworkIdle });
    await page.Locator("[data-testid='gantt']").WaitForAsync(new() { Timeout = 20_000 });
    await page.Locator("[data-testid^='gantt-bar-']").First.WaitForAsync(new() { Timeout = 20_000 });
}

static async Task DismissMessages(IPage page)
{
    // XAF shows the validation error as a toast/alert; close it so it cannot cover the chart.
    var close = page.Locator(".dxbl-toast button, .alert button.btn-close, .xaf-toast button");
    for (var i = await close.CountAsync() - 1; i >= 0; i--)
    {
        try { await close.Nth(i).ClickAsync(new() { Timeout = 1000 }); } catch { /* gone already */ }
    }
}

static async Task SetDarkTheme(IPage page)
{
    // Account menu (top right) -> appearance: the Fluent theme has a Light/Dark/System mode switch.
    await page.Locator(".theme-button").First.ClickAsync();
    await page.Locator(".themeswitcher-fluent-mode-dark").First.ClickAsync();
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    await page.WaitForTimeoutAsync(1500);
}

static async Task<IPage> NewPage(IBrowser browser, List<string> errors)
{
    var context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = 1600, Height = 1100 } });
    var page = await context.NewPageAsync();
    page.SetDefaultTimeout(30_000);
    page.SetDefaultNavigationTimeout(30_000);
    page.Console += (_, msg) => { if (msg.Type == "error") lock (errors) errors.Add("[console] " + msg.Text); };
    page.PageError += (_, err) => { lock (errors) errors.Add("[pageerror] " + err); };
    page.Response += (_, resp) => { if (resp.Status >= 400) lock (errors) errors.Add($"[http {resp.Status}] {resp.Url}"); };
    return page;
}

static Process StartApp(string blazorProj, System.Text.StringBuilder appOutput)
{
    // Development environment: static web assets (_content/...) only resolve from a build output there.
    var psi = new ProcessStartInfo("dotnet",
        $"run --no-build --no-launch-profile --project \"{blazorProj}\" -- --urls {BaseUrl} --environment Development")
    {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };
    var p = Process.Start(psi)!;
    p.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (appOutput) appOutput.AppendLine(e.Data); };
    p.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (appOutput) appOutput.AppendLine(e.Data); };
    p.BeginOutputReadLine();
    p.BeginErrorReadLine();
    return p;
}

static void KillApp(ref Process? app)
{
    if (app is null) return;
    try { app.Kill(entireProcessTree: true); app.WaitForExit(10_000); } catch { /* already gone */ }
    app.Dispose();
    app = null;
}

static async Task<bool> IsUp()
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
    try { await http.GetAsync(BaseUrl); return true; } catch { return false; }
}

static async Task WaitForHttpOk(System.Text.StringBuilder appOutput)
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    for (var i = 0; i < 60; i++)
    {
        try
        {
            var resp = await http.GetAsync(BaseUrl + "/LoginPage");
            if (resp.IsSuccessStatusCode) { Console.WriteLine($"    app ready at {BaseUrl}"); return; }
        }
        catch { /* not up yet */ }
        await Task.Delay(2000);
    }
    string tail;
    lock (appOutput) tail = string.Join('\n', appOutput.ToString().Split('\n').TakeLast(40));
    throw new Exception($"app did not become ready at {BaseUrl}\n--- app output (last 40 lines) ---\n{tail}");
}

static void RunOrThrow(string file, string args)
{
    var p = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false })!;
    p.WaitForExit();
    if (p.ExitCode != 0) throw new Exception($"`{file} {args}` exited with {p.ExitCode}");
}

static int Sql(string sql)
{
    using var conn = new SqlConnection(ConnectionString);
    conn.Open();
    using var cmd = new SqlCommand(sql, conn);
    return cmd.ExecuteNonQuery();
}

static string? SqlScalar(string sql)
{
    using var conn = new SqlConnection(ConnectionString);
    conn.Open();
    using var cmd = new SqlCommand(sql, conn);
    return cmd.ExecuteScalar()?.ToString();
}

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    for (; dir is not null; dir = dir.Parent)
        if (dir.GetFiles("*.sln").Length > 0) return dir.FullName;
    throw new Exception("repo root (.sln) not found above " + AppContext.BaseDirectory);
}
