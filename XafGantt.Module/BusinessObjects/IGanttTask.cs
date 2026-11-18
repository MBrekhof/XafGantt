namespace XafGantt.Module.BusinessObjects;

/// <summary>
/// What the Gantt list editor needs from a business object. Implement it explicitly so the
/// members do not show up as extra columns or view members.
/// </summary>
public interface IGanttTask {
    /// <summary>Unique and stable: the entity key as a string.</summary>
    string GanttId { get; }
    string GanttName { get; }
    /// <summary>First day of the work.</summary>
    DateTime GanttStart { get; set; }
    /// <summary>Last day of the work, inclusive.</summary>
    DateTime GanttEnd { get; set; }
    /// <summary>Null for a root task.</summary>
    string GanttParentId { get; }
    /// <summary>0..1, or null to hide the progress fill.</summary>
    double? GanttProgress { get; }
    string GanttCategory { get; }
    bool GanttIsMilestone { get; }
    IEnumerable<string> GanttDependsOn { get; }
}
