using Veto.Components.Gantt;
using XafGantt.Module.BusinessObjects;

namespace XafGantt.Blazor.Server.Editors.Gantt;

public static class GanttItemMapper {
    // ponytail: fixed window. A task outside it is left off the chart (it stays in the grid):
    // VetoGantt throws on DateOnly.MinValue (a task saved without dates) and draws one column per
    // day, so a typo year would build tens of thousands of columns. Make it a setting if needed.
    private const int YearsAroundToday = 10;

    /// <summary>
    /// Maps tasks to chart rows. A parent's bar spans its descendants (display only): Veto draws a
    /// summary row from the parent's own dates and never rolls children up.
    /// </summary>
    public static IReadOnlyList<VetoGanttItem> Map(IReadOnlyList<IGanttTask> all) {
        var from = DateTime.Today.AddYears(-YearsAroundToday);
        var to = DateTime.Today.AddYears(YearsAroundToday);
        var tasks = all.Where(t => t.GanttStart >= from && t.GanttStart <= to && t.GanttEnd >= from && t.GanttEnd <= to).ToList();
        var byId = tasks.ToDictionary(t => t.GanttId);

        // The parent as the chart will see it: null when it is not in the list, or when the task
        // sits in a parent cycle (Veto finds no root for such rows and silently drops them).
        string ParentOf(IGanttTask task) {
            var parentId = task.GanttParentId;
            if(parentId is null || !byId.ContainsKey(parentId)) {
                return null;
            }
            var seen = new HashSet<string>();
            for(var id = parentId; id is not null && byId.TryGetValue(id, out var ancestor) && seen.Add(id); id = ancestor.GanttParentId) {
                if(id == task.GanttId) {
                    return null;
                }
            }
            return parentId;
        }

        var parents = tasks.ToDictionary(t => t.GanttId, ParentOf);
        var children = tasks.Where(t => parents[t.GanttId] is not null).ToLookup(t => parents[t.GanttId]);
        var spans = new Dictionary<string, (DateOnly Start, DateOnly End)>();

        // Cycle members are roots by now, so this recursion ends.
        (DateOnly Start, DateOnly End) Span(IGanttTask task) {
            if(spans.TryGetValue(task.GanttId, out var known)) {
                return known;
            }
            var start = DateOnly.FromDateTime(task.GanttStart);
            var end = DateOnly.FromDateTime(task.GanttEnd);
            if(end < start) {
                end = start; // bad stored data: show a one-day bar rather than nothing
            }
            var span = (start, end);
            if(children[task.GanttId].Any()) {
                var kids = children[task.GanttId].Select(Span).ToList();
                span = (kids.Min(k => k.Start), kids.Max(k => k.End));
            }
            return spans[task.GanttId] = span;
        }

        return tasks.Select(task => {
            var (start, end) = Span(task);
            var isSummary = children[task.GanttId].Any();
            return new VetoGanttItem {
                Id = task.GanttId,
                Name = task.GanttName ?? "",
                Start = start,
                End = end,
                // Not the (DateTime, DateTime) constructor: it reads a midnight end as exclusive.
                StartTime = isSummary ? null : TimeOrNull(task.GanttStart),
                EndTime = isSummary ? null : TimeOrNull(task.GanttEnd),
                ParentId = parents[task.GanttId],
                Progress = task.GanttProgress,
                Category = task.GanttCategory,
                IsMilestone = task.GanttIsMilestone,
                DependsOn = task.GanttDependsOn.Where(byId.ContainsKey).ToList()
            };
        }).ToList();
    }

    private static TimeOnly? TimeOrNull(DateTime value) =>
        value.TimeOfDay == TimeSpan.Zero ? null : TimeOnly.FromDateTime(value);
}
