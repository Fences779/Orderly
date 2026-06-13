using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Services;
using Orderly.Data.Commerce.Repositories;
using Orderly.Data.Commerce.Services;
using Orderly.Data.Sqlite;
using Xunit;
using TaskStatus = Orderly.Core.Commerce.TaskStatus;

namespace Orderly.Tests.Commerce;

/// <summary>
/// Example/unit tests for the Task 12.4 CRUD services — <see cref="CommerceWorkspaceService"/>,
/// <see cref="CommerceUnitService"/>, <see cref="CommerceSupplierService"/>, and
/// <see cref="CommerceBusinessTaskService"/> (Req 4.1). They exercise the real SQLCipher-backed
/// Commerce repositories against an unencrypted temp database (no mocks) to verify create/read/
/// update/delete and, for business tasks, the status transitions that keep
/// <see cref="BusinessTask.CompletedAt"/> consistent with the task's status.
/// </summary>
public sealed class CrudServicesTests
{
    private static readonly DateTime AsOf = new(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    // --- IWorkspaceService ---

    [Fact]
    public async Task Workspace_create_read_update_delete_round_trips()
    {
        await WithFactoryAsync(async factory =>
        {
            var service = new CommerceWorkspaceService(new BusinessWorkspaceRepository(factory));

            var created = await service.CreateAsync(new BusinessWorkspace { Id = Guid.NewGuid(), Name = "工作区 A" });
            BusinessWorkspace? fetched = await service.GetByIdAsync(created.Id);
            Assert.NotNull(fetched);
            Assert.Equal("工作区 A", fetched!.Name);

            fetched.Name = "工作区 B";
            await service.UpdateAsync(fetched);
            Assert.Equal("工作区 B", (await service.GetByIdAsync(created.Id))!.Name);

            Assert.Single(await service.GetAllAsync());

            await service.DeleteAsync(created.Id);
            Assert.Null(await service.GetByIdAsync(created.Id));
            Assert.Empty(await service.GetAllAsync());
        });
    }

    [Fact]
    public async Task Workspace_create_with_null_throws()
    {
        await WithFactoryAsync(async factory =>
        {
            var service = new CommerceWorkspaceService(new BusinessWorkspaceRepository(factory));
            await Assert.ThrowsAsync<ArgumentNullException>(() => service.CreateAsync(null!));
        });
    }

    // --- IUnitService ---

    [Fact]
    public async Task Unit_create_read_update_delete_round_trips()
    {
        await WithFactoryAsync(async factory =>
        {
            var service = new CommerceUnitService(new UnitDefinitionRepository(factory));

            var created = await service.CreateAsync(new UnitDefinition
            {
                Id = Guid.NewGuid(),
                Code = "pcs",
                DisplayName = "个",
                IsBuiltIn = true,
            });

            UnitDefinition? fetched = await service.GetByIdAsync(created.Id);
            Assert.NotNull(fetched);
            Assert.Equal("个", fetched!.DisplayName);

            fetched.DisplayName = "件";
            await service.UpdateAsync(fetched);
            Assert.Equal("件", (await service.GetByIdAsync(created.Id))!.DisplayName);

            Assert.Single(await service.GetAllAsync());

            await service.DeleteAsync(created.Id);
            Assert.Null(await service.GetByIdAsync(created.Id));
        });
    }

    // --- ISupplierService ---

    [Fact]
    public async Task Supplier_create_read_update_delete_round_trips()
    {
        await WithFactoryAsync(async factory =>
        {
            var service = new CommerceSupplierService(new SupplierRepository(factory));
            Guid workspaceId = Guid.NewGuid();

            var created = await service.CreateAsync(new Supplier
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspaceId,
                Name = "供应商 A",
                Phone = "12345",
            });

            Supplier? fetched = await service.GetByIdAsync(created.Id);
            Assert.NotNull(fetched);
            Assert.Equal("供应商 A", fetched!.Name);

            fetched.Phone = "67890";
            await service.UpdateAsync(fetched);
            Assert.Equal("67890", (await service.GetByIdAsync(created.Id))!.Phone);

            Assert.Single(await service.GetAllAsync());

            await service.DeleteAsync(created.Id);
            Assert.Null(await service.GetByIdAsync(created.Id));
        });
    }

    // --- IBusinessTaskService ---

    [Fact]
    public async Task Task_create_read_update_delete_round_trips()
    {
        await WithFactoryAsync(async factory =>
        {
            var service = new CommerceBusinessTaskService(new BusinessTaskRepository(factory));
            Guid workspaceId = Guid.NewGuid();

            var created = await service.CreateAsync(new BusinessTask
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspaceId,
                Title = "回访客户",
            });

            BusinessTask? fetched = await service.GetByIdAsync(created.Id);
            Assert.NotNull(fetched);
            Assert.Equal(TaskStatus.Pending, fetched!.Status);

            fetched.Title = "回访客户 - 更新";
            await service.UpdateAsync(fetched);
            Assert.Equal("回访客户 - 更新", (await service.GetByIdAsync(created.Id))!.Title);

            await service.DeleteAsync(created.Id);
            Assert.Null(await service.GetByIdAsync(created.Id));
        });
    }

    [Fact]
    public async Task Task_transition_to_completed_stamps_completed_at()
    {
        await WithFactoryAsync(async factory =>
        {
            var service = new CommerceBusinessTaskService(new BusinessTaskRepository(factory));
            var created = await service.CreateAsync(new BusinessTask
            {
                Id = Guid.NewGuid(),
                WorkspaceId = Guid.NewGuid(),
                Title = "任务",
            });

            BusinessTask updated = await service.ChangeStatusAsync(created.Id, TaskStatus.Completed, AsOf);

            Assert.Equal(TaskStatus.Completed, updated.Status);
            Assert.Equal(AsOf, updated.CompletedAt);

            BusinessTask? persisted = await service.GetByIdAsync(created.Id);
            Assert.Equal(TaskStatus.Completed, persisted!.Status);
            Assert.Equal(AsOf, persisted.CompletedAt);
        });
    }

    [Fact]
    public async Task Task_transition_away_from_completed_clears_completed_at()
    {
        await WithFactoryAsync(async factory =>
        {
            var service = new CommerceBusinessTaskService(new BusinessTaskRepository(factory));
            var created = await service.CreateAsync(new BusinessTask
            {
                Id = Guid.NewGuid(),
                WorkspaceId = Guid.NewGuid(),
                Title = "任务",
            });

            await service.ChangeStatusAsync(created.Id, TaskStatus.Completed, AsOf);
            BusinessTask reopened = await service.ChangeStatusAsync(created.Id, TaskStatus.InProgress);

            Assert.Equal(TaskStatus.InProgress, reopened.Status);
            Assert.Null(reopened.CompletedAt);
            Assert.Null((await service.GetByIdAsync(created.Id))!.CompletedAt);
        });
    }

    [Fact]
    public async Task Task_transition_to_same_status_is_no_op()
    {
        await WithFactoryAsync(async factory =>
        {
            var service = new CommerceBusinessTaskService(new BusinessTaskRepository(factory));
            var created = await service.CreateAsync(new BusinessTask
            {
                Id = Guid.NewGuid(),
                WorkspaceId = Guid.NewGuid(),
                Title = "任务",
            });

            BusinessTask result = await service.ChangeStatusAsync(created.Id, TaskStatus.Pending);

            Assert.Equal(TaskStatus.Pending, result.Status);
            Assert.Null(result.CompletedAt);
        });
    }

    [Fact]
    public async Task Task_change_status_for_missing_task_throws()
    {
        await WithFactoryAsync(async factory =>
        {
            var service = new CommerceBusinessTaskService(new BusinessTaskRepository(factory));
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.ChangeStatusAsync(Guid.NewGuid(), TaskStatus.Completed));
        });
    }

    // --- Helpers ---

    private static async Task WithFactoryAsync(Func<SqliteConnectionFactory, Task> body)
    {
        string path = Path.Combine(Path.GetTempPath(), $"orderly-crud-{Guid.NewGuid():N}.db");
        try
        {
            var factory = new SqliteConnectionFactory(path);
            await new CommerceSchemaInitializer(factory).InitializeAsync();

            await body(factory);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (string file in new[] { path, path + "-wal", path + "-shm", path + "-journal" })
            {
                try
                {
                    if (File.Exists(file))
                    {
                        File.Delete(file);
                    }
                }
                catch (IOException)
                {
                    // Best-effort cleanup of temp files.
                }
            }
        }
    }
}
