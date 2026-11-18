using DevExpress.ExpressApp.EFCore.Updating;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using DevExpress.Persistent.BaseImpl.EF.PermissionPolicy;
using DevExpress.Persistent.BaseImpl.EF;
using DevExpress.ExpressApp.Design;
using DevExpress.ExpressApp.EFCore.DesignTime;

namespace XafGantt.Module.BusinessObjects;


[TypesInfoInitializer(typeof(DbContextTypesInfoInitializer<XafGanttEFCoreDbContext>))]
public class XafGanttEFCoreDbContext : DbContext {
    public XafGanttEFCoreDbContext(DbContextOptions<XafGanttEFCoreDbContext> options) : base(options) {
    }
    //public DbSet<ModuleInfo> ModulesInfo { get; set; }
    public DbSet<ModelDifference> ModelDifferences { get; set; }
    public DbSet<ModelDifferenceAspect> ModelDifferenceAspects { get; set; }
    public DbSet<PermissionPolicyRole> Roles { get; set; }
    public DbSet<XafGantt.Module.BusinessObjects.ApplicationUser> Users { get; set; }
    public DbSet<XafGantt.Module.BusinessObjects.ApplicationUserLoginInfo> UserLoginsInfo { get; set; }
    public DbSet<ProjectTask> ProjectTasks { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        base.OnModelCreating(modelBuilder);
        modelBuilder.UseDeferredDeletion(this);
        modelBuilder.UseOptimisticLock();
        modelBuilder.SetOneToManyAssociationDeleteBehavior(DeleteBehavior.SetNull, DeleteBehavior.Cascade);
        modelBuilder.HasChangeTrackingStrategy(ChangeTrackingStrategy.ChangingAndChangedNotificationsWithOriginalValues);
        modelBuilder.UsePropertyAccessMode(PropertyAccessMode.PreferFieldDuringConstruction);
        modelBuilder.Entity<XafGantt.Module.BusinessObjects.ApplicationUserLoginInfo>(b => {
            b.HasIndex(nameof(DevExpress.ExpressApp.Security.ISecurityUserLoginInfo.LoginProviderName), nameof(DevExpress.ExpressApp.Security.ISecurityUserLoginInfo.ProviderUserKey)).IsUnique();
        });
        // Self references: SQL Server refuses cascade or set-null actions on them ("may cause cycles
        // or multiple cascade paths"), so the database gets NO ACTION and EF does the work.
        modelBuilder.Entity<ProjectTask>(b => {
            b.HasOne(t => t.Parent).WithMany(t => t.Children).HasForeignKey(t => t.ParentID).OnDelete(DeleteBehavior.ClientSetNull);
            b.HasMany(t => t.Predecessors).WithMany(t => t.Successors).UsingEntity<Dictionary<string, object>>(
                "ProjectTaskDependency",
                r => r.HasOne<ProjectTask>().WithMany().HasForeignKey("PredecessorID").OnDelete(DeleteBehavior.ClientCascade),
                l => l.HasOne<ProjectTask>().WithMany().HasForeignKey("SuccessorID").OnDelete(DeleteBehavior.ClientCascade));
        });
        modelBuilder.Entity<ModelDifference>()
            .HasMany(t => t.Aspects)
            .WithOne(t => t.Owner)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
