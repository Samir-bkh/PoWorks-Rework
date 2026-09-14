using Xunit;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PoWorks_Rework.Controllers;
using PoWorks_Rework.Data;
using PoWorks_Rework.Services;

namespace PoWorks_Rework.Tests;

public class UserLifecycleTests
{
    [Fact]
    public async Task DisableUser_PersistsLockout()
    {
        await using var fixture = await TestFixture.CreateAsync("disable");
        var user = await fixture.CreateUserAsync("test.disable");

        await fixture.Controller.DisableUser(user.Id);

        var reloaded = await fixture.UserManager.FindByIdAsync(user.Id);
        Assert.NotNull(reloaded);
        Assert.True(reloaded!.LockoutEnabled);
        Assert.True(reloaded.LockoutEnd > DateTimeOffset.UtcNow);
        Assert.True(await fixture.UserManager.IsLockedOutAsync(reloaded));
    }

    [Fact]
    public async Task EnableUser_ClearsEffectiveLockout()
    {
        await using var fixture = await TestFixture.CreateAsync("enable");
        var user = await fixture.CreateUserAsync("test.enable");

        await fixture.Controller.DisableUser(user.Id);
        await fixture.Controller.EnableUser(user.Id);

        var reloaded = await fixture.UserManager.FindByIdAsync(user.Id);
        Assert.NotNull(reloaded);
        Assert.True(reloaded!.LockoutEnabled);
        Assert.True(reloaded.LockoutEnd <= DateTimeOffset.UtcNow);
        Assert.False(await fixture.UserManager.IsLockedOutAsync(reloaded));
        Assert.Equal(0, reloaded.AccessFailedCount);
    }

    [Fact]
    public async Task Admin_CannotBeDisabled()
    {
        await using var fixture = await TestFixture.CreateAsync("admin");
        var admin = await fixture.CreateUserAsync("Admin");

        await fixture.Controller.DisableUser(admin.Id);

        var reloaded = await fixture.UserManager.FindByIdAsync(admin.Id);
        Assert.NotNull(reloaded);
        Assert.False(await fixture.UserManager.IsLockedOutAsync(reloaded!));
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        public ServiceProvider Services { get; }
        public UserManager<IdentityUser> UserManager { get; }
        public UserManagementController Controller { get; }

        private TestFixture(ServiceProvider services, UserManager<IdentityUser> userManager, UserManagementController controller)
        {
            Services = services;
            UserManager = userManager;
            Controller = controller;
        }

        public static Task<TestFixture> CreateAsync(string databaseSuffix)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase($"poworks-tests-{databaseSuffix}-{Guid.NewGuid()}"));

            services
                .AddIdentity<IdentityUser, IdentityRole>(options =>
                {
                    options.SignIn.RequireConfirmedAccount = false;
                })
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddDefaultTokenProviders();

            var provider = services.BuildServiceProvider();
            var userManager = provider.GetRequiredService<UserManager<IdentityUser>>();

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["EncryptionKey"] = "test-encryption-key",
                    ["DatabaseSettings:Host"] = "localhost",
                    ["DatabaseSettings:Port"] = "5432",
                    ["DatabaseSettings:Database"] = "",
                    ["DatabaseSettings:Username"] = "postgres",
                    ["DatabaseSettings:Password"] = ""
                })
                .Build();

            var encryption = new EncryptionService(configuration);
            var databaseService = new DatabaseService(configuration, encryption);
            var controller = new UserManagementController(userManager, databaseService, new FixedCompanyContext());

            return Task.FromResult(new TestFixture(provider, userManager, controller));
        }

        public async Task<IdentityUser> CreateUserAsync(string username)
        {
            var user = new IdentityUser
            {
                UserName = username,
                LockoutEnabled = true
            };

            var result = await UserManager.CreateAsync(user);
            Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
            return user;
        }

        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
        }
    }

    private sealed class FixedCompanyContext : ICompanyContext
    {
        public int CurrentCompanyId => 1;
    }
}
