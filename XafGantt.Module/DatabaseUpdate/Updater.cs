using DevExpress.ExpressApp;
using DevExpress.Data.Filtering;
using DevExpress.Persistent.Base;
using DevExpress.ExpressApp.Updating;
using DevExpress.ExpressApp.Security;
using DevExpress.ExpressApp.SystemModule;
using DevExpress.ExpressApp.EF;
using DevExpress.Persistent.BaseImpl.EF;
using DevExpress.Persistent.BaseImpl.EF.PermissionPolicy;
using XafGantt.Module.BusinessObjects;
using Microsoft.Extensions.DependencyInjection;

namespace XafGantt.Module.DatabaseUpdate;

// For more typical usage scenarios, be sure to check out https://docs.devexpress.com/eXpressAppFramework/DevExpress.ExpressApp.Updating.ModuleUpdater
public class Updater : ModuleUpdater {
    public Updater(IObjectSpace objectSpace, Version currentDBVersion) :
        base(objectSpace, currentDBVersion) {
    }
    public override void UpdateDatabaseAfterUpdateSchema() {
        base.UpdateDatabaseAfterUpdateSchema();
        //string name = "MyName";
        //EntityObject1 theObject = ObjectSpace.FirstOrDefault<EntityObject1>(u => u.Name == name);
        //if(theObject == null) {
        //    theObject = ObjectSpace.CreateObject<EntityObject1>();
        //    theObject.Name = name;
        //}

        // The code below creates users and roles for testing purposes only.
        // In production code, you can create users and assign roles to them automatically, as described in the following help topic:
        // https://docs.devexpress.com/eXpressAppFramework/119064/data-security-and-safety/security-system/authentication
#if !RELEASE
        // If a role doesn't exist in the database, create this role
        var defaultRole = CreateDefaultRole();
        var adminRole = CreateAdminRole();

        ObjectSpace.CommitChanges(); //This line persists created object(s).

        UserManager userManager = ObjectSpace.ServiceProvider.GetRequiredService<UserManager>();

        // If a user named 'User' doesn't exist in the database, create this user
        if(userManager.FindUserByName<ApplicationUser>(ObjectSpace, "User") == null) {
            // Set a password if the standard authentication type is used
            string EmptyPassword = "";
            _ = userManager.CreateUser<ApplicationUser>(ObjectSpace, "User", EmptyPassword, (user) => {
                // Add the Users role to the user
                user.Roles.Add(defaultRole);
            });
        }

        // If a user named 'Admin' doesn't exist in the database, create this user
        if(userManager.FindUserByName<ApplicationUser>(ObjectSpace, "Admin") == null) {
            // Set a password if the standard authentication type is used
            string EmptyPassword = "";
            _ = userManager.CreateUser<ApplicationUser>(ObjectSpace, "Admin", EmptyPassword, (user) => {
                // Add the Administrators role to the user
                user.Roles.Add(adminRole);
            });
        }

        // 'Viewer' may read tasks but not change them: the Gantt must be read-only for this user.
        var viewerRole = ObjectSpace.FirstOrDefault<PermissionPolicyRole>(r => r.Name == "Viewers");
        if(viewerRole == null) {
            viewerRole = ObjectSpace.CreateObject<PermissionPolicyRole>();
            viewerRole.Name = "Viewers";
            viewerRole.AddTypePermissionsRecursively<ProjectTask>(SecurityOperations.Read, SecurityPermissionState.Allow);
            viewerRole.AddNavigationPermission(@"Application/NavigationItems/Items/Planning", SecurityPermissionState.Allow);
        }
        if(userManager.FindUserByName<ApplicationUser>(ObjectSpace, "Viewer") == null) {
            _ = userManager.CreateUser<ApplicationUser>(ObjectSpace, "Viewer", "", (user) => {
                user.Roles.Add(defaultRole);
                user.Roles.Add(viewerRole);
            });
        }

        ObjectSpace.CommitChanges(); //This line persists created object(s).
#endif
        SeedTasks();
    }

    // Seeded relative to the Monday of the current week, so weekdays are the same on every run.
    void SeedTasks() {
        if(ObjectSpace.FirstOrDefault<ProjectTask>(t => true) != null) {
            return;
        }
        var today = DateTime.Today;
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));

        ProjectTask Add(string name, ProjectTask parent, int startOffset, int days, int progress, string category, params ProjectTask[] predecessors) {
            var task = ObjectSpace.CreateObject<ProjectTask>();
            task.Name = name;
            task.Parent = parent;
            task.StartDate = monday.AddDays(startOffset);
            task.EndDate = monday.AddDays(startOffset + days - 1);
            task.Progress = progress;
            task.Category = category;
            foreach(var predecessor in predecessors) {
                task.Predecessors.Add(predecessor);
            }
            return task;
        }

        var website = Add("Website relaunch", null, -7, 33, 40, "Project");
        var design = Add("Design", website, -7, 12, 80, "Design");
        var wireframes = Add("Wireframes", design, -7, 5, 100, "Design");
        var visual = Add("Visual design", design, 0, 5, 60, "Design", wireframes);
        var build = Add("Build", website, 2, 17, 20, "Engineering");
        var frontend = Add("Front end", build, 2, 10, 30, "Engineering", wireframes);
        var backend = Add("Back end", build, 4, 12, 15, "Engineering");
        var integration = Add("Integration", build, 16, 3, 0, "Engineering", frontend, backend);
        var content = Add("Content migration", website, 9, 6, 0, "Content");
        var testing = Add("Acceptance test", website, 21, 4, 0, "QA", integration, content);
        var goLive = Add("Go live", website, 25, 1, 0, "Milestone", testing);
        goLive.IsMilestone = true;

        var lab = Add("Lab instrument rollout", null, 0, 19, 10, "Project");
        var purchase = Add("Purchase", lab, 0, 4, 100, "Procurement");
        var install = Add("Installation", lab, 7, 3, 0, "Engineering", purchase);
        var validation = Add("Validation", lab, 10, 5, 0, "QA", install);
        Add("Training", lab, 15, 4, 0, "Content", validation);

        ObjectSpace.CommitChanges();
    }
    public override void UpdateDatabaseBeforeUpdateSchema() {
        base.UpdateDatabaseBeforeUpdateSchema();
    }
    PermissionPolicyRole CreateAdminRole() {
        PermissionPolicyRole adminRole = ObjectSpace.FirstOrDefault<PermissionPolicyRole>(r => r.Name == "Administrators");
        if(adminRole == null) {
            adminRole = ObjectSpace.CreateObject<PermissionPolicyRole>();
            adminRole.Name = "Administrators";
            adminRole.IsAdministrative = true;
        }
        return adminRole;
    }
    PermissionPolicyRole CreateDefaultRole() {
        PermissionPolicyRole defaultRole = ObjectSpace.FirstOrDefault<PermissionPolicyRole>(role => role.Name == "Default");
        if(defaultRole == null) {
            defaultRole = ObjectSpace.CreateObject<PermissionPolicyRole>();
            defaultRole.Name = "Default";

            defaultRole.AddObjectPermissionFromLambda<ApplicationUser>(SecurityOperations.Read, cm => cm.ID == (Guid)CurrentUserIdOperator.CurrentUserId(), SecurityPermissionState.Allow);
            defaultRole.AddNavigationPermission(@"Application/NavigationItems/Items/Default/Items/MyDetails", SecurityPermissionState.Allow);
            defaultRole.AddMemberPermissionFromLambda<ApplicationUser>(SecurityOperations.Write, "ChangePasswordOnFirstLogon", cm => cm.ID == (Guid)CurrentUserIdOperator.CurrentUserId(), SecurityPermissionState.Allow);
            defaultRole.AddMemberPermissionFromLambda<ApplicationUser>(SecurityOperations.Write, "StoredPassword", cm => cm.ID == (Guid)CurrentUserIdOperator.CurrentUserId(), SecurityPermissionState.Allow);
            defaultRole.AddTypePermissionsRecursively<PermissionPolicyRole>(SecurityOperations.Read, SecurityPermissionState.Deny);
            defaultRole.AddObjectPermission<ModelDifference>(SecurityOperations.ReadWriteAccess, "UserId = ToStr(CurrentUserId())", SecurityPermissionState.Allow);
            defaultRole.AddObjectPermission<ModelDifferenceAspect>(SecurityOperations.ReadWriteAccess, "Owner.UserId = ToStr(CurrentUserId())", SecurityPermissionState.Allow);
            defaultRole.AddTypePermissionsRecursively<ModelDifference>(SecurityOperations.Create, SecurityPermissionState.Allow);
            defaultRole.AddTypePermissionsRecursively<ModelDifferenceAspect>(SecurityOperations.Create, SecurityPermissionState.Allow);
        }
        return defaultRole;
    }
}
