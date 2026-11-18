using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using DevExpress.ExpressApp.DC;
using DevExpress.ExpressApp.Model;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF;
using DevExpress.Persistent.Validation;

namespace XafGantt.Module.BusinessObjects;

[DefaultClassOptions]
[NavigationItem("Planning")]
[ImageName("BO_Task")]
[DefaultProperty(nameof(Name))]
[ModelDefault("Caption", "Task")]
public class ProjectTask : BaseObject, IGanttTask {
    [RuleRequiredField]
    public virtual string Name { get; set; }

    public virtual DateTime StartDate { get; set; }

    /// <summary>Last day of the work, inclusive.</summary>
    public virtual DateTime EndDate { get; set; }

    /// <summary>0..100.</summary>
    [RuleRange(0, 100)]
    public virtual int Progress { get; set; }

    public virtual string Category { get; set; }

    public virtual bool IsMilestone { get; set; }

    [FieldSize(FieldSizeAttribute.Unlimited)]
    public virtual string Description { get; set; }

    [Browsable(false)]
    public virtual Guid? ParentID { get; set; }

    [ForeignKey(nameof(ParentID))]
    public virtual ProjectTask Parent { get; set; }

    public virtual IList<ProjectTask> Children { get; set; } = new ObservableCollection<ProjectTask>();

    public virtual IList<ProjectTask> Predecessors { get; set; } = new ObservableCollection<ProjectTask>();

    public virtual IList<ProjectTask> Successors { get; set; } = new ObservableCollection<ProjectTask>();

    [NotMapped, Browsable(false)]
    [RuleFromBoolProperty("ProjectTask_EndNotBeforeStart", DefaultContexts.Save, "The end date cannot be before the start date.", UsedProperties = nameof(EndDate))]
    public bool EndNotBeforeStart => EndDate.Date >= StartDate.Date;

    // The rule a drag in the chart can actually break: the chart itself clamps end >= start.
    [NotMapped, Browsable(false)]
    [RuleFromBoolProperty("ProjectTask_StartsOnWorkday", DefaultContexts.Save, "A task cannot start in a weekend.", UsedProperties = nameof(StartDate))]
    public bool StartsOnWorkday => StartDate.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

    [NotMapped, Browsable(false)]
    [RuleFromBoolProperty("ProjectTask_NoParentCycle", DefaultContexts.Save, "A task cannot be its own parent or the parent of one of its ancestors.", UsedProperties = nameof(Parent))]
    public bool ParentIsNotSelfOrDescendant {
        get {
            var seen = new HashSet<ProjectTask>();
            for(var ancestor = Parent; ancestor is not null && seen.Add(ancestor); ancestor = ancestor.Parent) {
                if(ReferenceEquals(ancestor, this)) {
                    return false;
                }
            }
            return true;
        }
    }

    // A new task gets today's date: an unset date is 1 January 0001, which no chart can draw.
    public override void OnCreated() {
        base.OnCreated();
        StartDate = EndDate = DateTime.Today;
    }

    string IGanttTask.GanttId => ID.ToString();
    string IGanttTask.GanttName => Name;
    DateTime IGanttTask.GanttStart { get => StartDate; set => StartDate = value; }
    DateTime IGanttTask.GanttEnd { get => EndDate; set => EndDate = value; }
    string IGanttTask.GanttParentId => ParentID?.ToString();
    double? IGanttTask.GanttProgress => Progress / 100d;
    string IGanttTask.GanttCategory => Category;
    bool IGanttTask.GanttIsMilestone => IsMilestone;
    IEnumerable<string> IGanttTask.GanttDependsOn => Predecessors.Select(p => p.ID.ToString());
}
