using System.Collections;
using System.ComponentModel;
using DevExpress.Blazor;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Blazor;
using DevExpress.ExpressApp.Blazor.Components;
using DevExpress.ExpressApp.Blazor.Components.Models;
using DevExpress.ExpressApp.Blazor.Services;
using DevExpress.ExpressApp.Editors;
using DevExpress.ExpressApp.Model;
using DevExpress.ExpressApp.Security;
using DevExpress.ExpressApp.SystemModule;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Veto.Components.Gantt;
using XafGantt.Module.BusinessObjects;

namespace XafGantt.Blazor.Server.Editors.Gantt;

/// <summary>
/// Shows any <see cref="IGanttTask"/> list as a Veto Gantt chart. Not the default editor: assign it
/// to a List View in the Model Editor (EditorType).
/// Pattern: dxdocs "How to: Use a Custom Component to Implement List Editor (Blazor)" (403258).
/// </summary>
[ListEditor(typeof(IGanttTask), false)]
public class GanttListEditor : ListEditor, IComponentContentHolder, IControlOrderProvider, IComplexListEditor {
    private RenderFragment componentContent;
    private CollectionSourceBase collectionSource;
    private XafApplication application;
    private IThemeService themeService;
    private IBindingList boundList;
    private List<IGanttTask> tasks = [];
    private Dictionary<string, IGanttTask> tasksById = [];
    private IGanttTask selected;

    public GanttListEditor(IModelListView model) : base(model) { }

    public GanttListViewModel ComponentModel { get; private set; }

    public RenderFragment ComponentContent =>
        componentContent ??= ComponentModelObserver.Create(ComponentModel, ComponentModel.GetComponentContent());

    public void Setup(CollectionSourceBase collectionSource, XafApplication application) {
        this.collectionSource = collectionSource;
        this.application = application;
        themeService = application.ServiceProvider.GetService<IThemeService>();
    }

    protected override object CreateControlsCore() {
        ComponentModel = new GanttListViewModel {
            Editable = CanEditTasks(),
            Dark = IsDark(),
            SelectedIdChanged = EventCallback.Factory.Create<string>(this, OnChartSelectionChanged),
            OnItemChanged = EventCallback.Factory.Create<VetoGanttChange>(this, OnChartItemChanged),
            OnOpen = EventCallback.Factory.Create<string>(this, OnChartOpen)
        };
        if(themeService is not null) {
            themeService.CurrentThemeChanged += ThemeService_CurrentThemeChanged;
        }
        return ComponentModel;
    }

    protected override void OnAllowEditChanged() {
        base.OnAllowEditChanged();
        if(ComponentModel is not null) {
            ComponentModel.Editable = CanEditTasks();
        }
    }

    // AllowEdit carries the model setting only: with the standard (request-based) security system
    // ListView.UpdateSecurityModifiers skips the type-level check, so ask for it here. Object-level
    // denials still surface at commit and are rolled back.
    private bool CanEditTasks() =>
        AllowEdit && (application.Security is not IRequestSecurityStrategy security
                      || security.CanWrite(collectionSource.ObjectTypeInfo.Type, collectionSource.ObjectSpace));

    protected override void AssignDataSourceToControl(object dataSource) {
        if(boundList is not null) {
            boundList.ListChanged -= BindingList_ListChanged;
        }
        boundList = dataSource as IBindingList;
        if(boundList is not null) {
            boundList.ListChanged += BindingList_ListChanged;
        }
        UpdateItems(dataSource);
    }

    private void BindingList_ListChanged(object sender, ListChangedEventArgs e) => UpdateItems(DataSource);

    public override void Refresh() => UpdateItems(DataSource);

    public override void BreakLinksToControls() {
        Unsubscribe();
        selected = null; // no selection event while the view is being torn down
        UpdateItems(null);
        componentContent = null; // a recreated control gets a new ComponentModel
        base.BreakLinksToControls();
    }

    // ListView replaces an editor with Dispose() alone, without BreakLinksToControls.
    public override void Dispose() {
        Unsubscribe();
        base.Dispose();
    }

    private void Unsubscribe() {
        if(themeService is not null) {
            themeService.CurrentThemeChanged -= ThemeService_CurrentThemeChanged;
        }
        if(boundList is not null) {
            boundList.ListChanged -= BindingList_ListChanged;
            boundList = null;
        }
    }

    private void UpdateItems(object dataSource) {
        tasks = (dataSource as IEnumerable)?.OfType<IGanttTask>().ToList() ?? [];
        tasksById = tasks.ToDictionary(t => t.GanttId);
        // A reload (Refresh, Rollback) replaces the instances: point the selection at the new one,
        // or drop it when the task is gone.
        if(selected is not null) {
            tasksById.TryGetValue(selected.GanttId, out var current);
            if(!ReferenceEquals(current, selected)) {
                selected = current;
                OnSelectionChanged();
                OnFocusedObjectChanged();
            }
        }
        if(ComponentModel is not null) {
            ComponentModel.Items = GanttItemMapper.Map(tasks);
            ComponentModel.SelectedId = selected?.GanttId;
        }
    }

    #region Selection

    public override SelectionType SelectionType => SelectionType.Full;

    public override IList GetSelectedObjects() => selected is null ? Array.Empty<object>() : new object[] { selected };

    public override object FocusedObject {
        get => selected;
        set => Select(value as IGanttTask);
    }

    private void Select(IGanttTask task) {
        if(ReferenceEquals(selected, task)) {
            return;
        }
        selected = task;
        if(ComponentModel is not null) {
            ComponentModel.SelectedId = task?.GanttId;
        }
        OnSelectionChanged();
        OnFocusedObjectChanged();
    }

    private void OnChartSelectionChanged(string id) =>
        Select(id is not null && tasksById.TryGetValue(id, out var task) ? task : null);

    // The chart does not select the row a context menu was opened on, so select it first.
    private void OnChartOpen(string id) {
        if(tasksById.TryGetValue(id, out var task)) {
            Select(task);
            OnProcessSelectedItem();
        }
    }

    #endregion

    #region Drag to reschedule

    private void OnChartItemChanged(VetoGanttChange change) {
        // Deleted in another view while the chart was open: nothing to change; the wrapper resets the bar.
        if(!tasksById.TryGetValue(change.Id, out var task)) {
            return;
        }
        var objectSpace = collectionSource.ObjectSpace;
        try {
            // The chart reports whole days; the stored time of day is kept.
            if(change.Edit is VetoGanttEdit.Moved or VetoGanttEdit.StartChanged) {
                task.GanttStart = change.Start.ToDateTime(TimeOnly.FromDateTime(task.GanttStart));
            }
            if(change.Edit is VetoGanttEdit.Moved or VetoGanttEdit.EndChanged) {
                task.GanttEnd = change.End.ToDateTime(TimeOnly.FromDateTime(task.GanttEnd));
            }
            objectSpace.CommitChanges();
            UpdateItems(DataSource);
        }
        catch(Exception ex) {
            // Validation, security or concurrency said no: tell the user and put the stored values
            // back. The rollback reloads the collection, which refreshes the chart's items.
            var handler = application.ServiceProvider.GetRequiredService<IExceptionHandlerService>();
            handler.HandleException(ex);
            try {
                objectSpace.Rollback(false);
            }
            catch(Exception rollbackException) {
                handler.HandleException(rollbackException); // e.g. the database is unreachable
            }
        }
    }

    #endregion

    #region Theme

    private void ThemeService_CurrentThemeChanged(object sender, EventArgs e) {
        if(ComponentModel is not null) {
            ComponentModel.Dark = IsDark();
        }
    }

    // XAF's own IsDark is internal (IThemeInternalService). A classic theme, when one is active,
    // wins over the Fluent theme; among the classic themes only Blazing Dark is dark.
    private bool IsDark() {
        if(themeService is null) {
            return false;
        }
        if(themeService.CurrentTheme is { } classic) {
            return classic.Url?.Contains("dark", StringComparison.OrdinalIgnoreCase) ?? false;
        }
        return themeService.CurrentFluentTheme?.Mode == ThemeMode.Dark;
    }

    #endregion

    #region IControlOrderProvider (Previous/Next in the Detail View)

    public int GetIndexByObject(object obj) => obj is IGanttTask task ? tasks.IndexOf(task) : -1;

    public object GetObjectByIndex(int index) => index >= 0 && index < tasks.Count ? tasks[index] : null;

    public IList GetOrderedObjects() => tasks.Cast<object>().ToList();

    #endregion
}
