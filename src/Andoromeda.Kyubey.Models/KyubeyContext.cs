﻿using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Pomelo.AspNetCore.Extensions.BlobStorage.Models;
using Newtonsoft.Json;
using Andoromeda.Kyubey.Models;

namespace Andoromeda.Kyubey.Models
{
    public class KyubeyContext : IdentityDbContext<User, UserRole, long>, IBlobStorageDbContext
    {
        public KyubeyContext(DbContextOptions options) : base(options)
        {
        }

        protected KyubeyContext()
        {
        }

        /// <summary>
        /// Initialize data
        /// </summary>
        /// <param name="userManager"></param>
        /// <param name="roleManager"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task InitializeDatabaseAsync(
            UserManager<User> userManager, 
            RoleManager<UserRole> roleManager,
            CancellationToken token = default)
        {
            await Database.EnsureCreatedAsync();

            if (await roleManager.FindByNameAsync("ROOT") == null)
            {
                await roleManager.CreateAsync(new UserRole { Name = "ROOT", NormalizedName = "ROOT" });
            }

            if (await userManager.FindByNameAsync("root") == null)
            {
                var user = new User { UserName = "root" };
                await userManager.CreateAsync(user, "123456");
                await userManager.AddToRoleAsync(user, "ROOT");
            }

            if (!await Curves.AnyAsync(x => x.Id == "bancor", token))
            {
                Curves.Add(new Curve
                {
                    Id = "bancor",
                    PriceSupplyFunction = "${supply} * (pow((1 + (x - ${balance}) / ${balance}),${cw}) - 1) + ${supply}",
                    BalanceSupplyFunction = "x / ((${supply} * (pow((1 + (x - ${balance}) / ${balance}),${cw}) - 1) + ${supply}) * ${cw})",
                    Description = "",
                    Arguments = JsonConvert.SerializeObject(new List<CurveArgument>
                    {
                        new CurveArgument{ Id = "supply", ControlType = CurveArgumentControlType.Input, Name = "Initial Supply (Token)" },
                        new CurveArgument{ Id = "balance", ControlType = CurveArgumentControlType.Input, Name = "Initial Balance (EOS)" },
                        new CurveArgument{ Id = "cw", ControlType = CurveArgumentControlType.Slider, Name = "Reserve Ratio" }
                    })
                });

                await SaveChangesAsync();
            }

            if (!await Curves.AnyAsync(x => x.Id == "kyubey", token))
            {
                Curves.Add(new Curve
                {
                    Id = "kyubey",
                    PriceSupplyFunction = "${k} * x",
                    BalanceSupplyFunction = "${k}/2 * x^2",
                    SupplyBalanceFunction = "(2/${k})^(0.5) * x^(0.5)",
                    PriceBalanceFunction = "((2/${k})^(0.5) * x^(0.5)) * ${k}",
                    Description = "",
                    Arguments = JsonConvert.SerializeObject(new List<CurveArgument>
                    {
                        new CurveArgument{ Id = "k", ControlType = CurveArgumentControlType.Slider, Name = "Increase Ratio" }
                    })
                });

                await SaveChangesAsync();
            }
        }

        public DbSet<Blob> Blobs { get; set; }

        public DbSet<Token> Tokens { get; set; }

        public DbSet<Otc> Otcs { get; set; }

        public DbSet<Bancor> Bancors { get; set; }

        public DbSet<AlertRule> AlertRules { get; set; }

        public DbSet<AlertLog> AlertLogs { get; set; }

        public DbSet<Curve> Curves { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.SetupBlobStorage();

            builder.Entity<Token>(e =>
            {
                e.HasIndex(x => x.Priority);
                e.HasIndex(x => x.Name).ForMySqlIsFullText();
            });

            builder.Entity<Bancor>(e =>
            {
                e.HasIndex(x => x.Status);
            });

            builder.Entity<Otc>(e =>
            {
                e.HasIndex(x => x.Status);
            });
        }
    }
}