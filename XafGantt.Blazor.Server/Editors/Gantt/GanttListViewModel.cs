using DevExpress.ExpressApp.Blazor.Components.Models;
using Microsoft.AspNetCore.Components;
using Veto.Components.Gantt;

namespace XafGantt.Blazor.Server.Editors.Gantt;

public class GanttListViewModel : ComponentModelBase {
    public IReadOnlyList<VetoGanttItem> Items {
        get => GetPropertyValue<IReadOnlyList<VetoGanttItem>>();
        set => SetPropertyValue(value);
    }
    public bool Editable {
        get => GetPropertyValue<bool>();
        set => SetPropertyValue(value);
    }
    public bool Dark {
        get => GetPropertyValue<bool>();
        set => SetPropertyValue(value);
    }
    public string SelectedId {
        get => GetPropertyValue<string>();
        set => SetPropertyValue(value);
    }
    public EventCallback<string> SelectedIdChanged {
        get => GetPropertyValue<EventCallback<string>>();
        set => SetPropertyValue(value);
    }
    public EventCallback<VetoGanttChange> OnItemChanged {
        get => GetPropertyValue<EventCallback<VetoGanttChange>>();
        set => SetPropertyValue(value);
    }
    public EventCallback<string> OnOpen {
        get => GetPropertyValue<EventCallback<string>>();
        set => SetPropertyValue(value);
    }
    public override Type ComponentType => typeof(GanttListView);
}
