using Microsoft.EntityFrameworkCore;

namespace MachineStatusUpdate.Models
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
               : base(options)
        {
        }

        public DbSet<SVN_Equipment_Info_History> SVN_Equipment_Info_History { get; set; }

        public DbSet<SVN_Equipment_Info_History_Test> SVN_Equipment_Info_History_Test { get; set; }

        public DbSet<SVN_Equipment_Machine_Info> sVN_Equipment_Machine_Info { get; set; }

        public DbSet<SVN_Equipment_Status_Update_Detail> SVN_Equipment_Status_Update_Detail { get; set; }

        // public DbSet<SVN_Downtime_Info> SVN_Downtime_Infos { get; set; }

        public DbSet<SVN_Downtime_Info_Devel> SVN_Downtime_Infos_Devel { get; set; }


        public DbSet<SVN_target> SVN_targets { get; set; }

        public DbSet<SVN_Downtime_Reason> SVN_Downtime_Reasons { get; set; }

        public DbSet<SVN_Downtime_Account> SVN_Downtime_Accounts { get; set; }

        public DbSet<SVN_Downtime_TechResponse> SVN_Downtime_TechResponses { get; set; }
        
        // SM tables
        public DbSet<SM_Downtime_Reason> SM_Downtime_Reasons { get; set; }

        public DbSet<SM_EmployInfo> SM_EmployInfos { get; set; }

        public DbSet<SM_Operation> SM_Operations { get; set; }

        
        public DbSet<SVN_Defect_Cookie> SVN_Defect_Cookies { get; set; }



        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<SVN_target>().HasNoKey().ToTable("SVN_target");
            modelBuilder.Entity<SVN_target>().Property(x => x.Operation).HasColumnName("Operation");
            modelBuilder.Entity<SVN_target>().Property(x => x.Date_time).HasColumnName("Date_time");
        }

    }

}
