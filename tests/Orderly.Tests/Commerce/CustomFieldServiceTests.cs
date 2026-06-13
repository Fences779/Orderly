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

namespace Orderly.Tests.Commerce;

/// <summary>
/// Example/unit tests for the Task 13.4 <see cref="CommerceCustomFieldService"/> (Req 5.4). They
/// exercise the real SQLCipher-backed Commerce repository against an unencrypted temp database (no
/// mocks) to verify the 0–100 per-entity-type bound, single-entity-type association, per-entity-type
/// and per-template independence, and CRUD.
/// </summary>
public sealed class CustomFieldServiceTests
{
    [Fact]
    public async Task Add_up_to_one_hundred_definitions_per_entity_type_succeeds()
    {
        await WithFactoryAsync(async factory =>
        {
            var service = new CommerceCustomFieldService(new CustomFieldDefinitionRepository(factory));
            Guid templateId = Guid.NewGuid();

            for (int i = 0; i < ICustomFieldService.MaxDefinitionsPerEntityType; i++)
            {
                CustomFieldDefinitionResult result = await service.AddDefinitionAsync(
                    NewDefinition(templateId, BusinessEntityType.Order, $"field_{i}"));
                Assert.True(result.IsAdded, result.Error);
            }

            Assert.Equal(
                ICustomFieldServiceMax,
                (await service.GetByEntityTypeAsync(templateId, BusinessEntityType.Order)).Count);
        });
    }

    [Fact]
    public async Task Adding_one_hundred_first_definition_is_rejected_with_limit_exceeded()
    {
        await WithFactoryAsync(async factory =>
        {
            var service = new CommerceCustomFieldService(new CustomFieldDefinitionRepository(factory));
            Guid templateId = Guid.NewGuid();

            for (int i = 0; i < ICustomFieldServiceMax; i++)
            {
                await service.AddDefinitionAsync(NewDefinition(templateId, BusinessEntityType.Order, $"field_{i}"));
            }

            CustomFieldDefinitionResult result = await service.AddDefinitionAsync(
                NewDefinition(templateId, BusinessEntityType.Order, "field_overflow"));

            Assert.False(result.IsAdded);
            Assert.True(result.IsLimitExceeded);
            Assert.Equal(CustomFieldDefinitionOutcome.CustomFieldLimitExceeded, result.Outcome);
            Assert.Null(result.Definition);

            // The rejected add persisted nothing: the count is still exactly the maximum (Req 5.4).
            Assert.Equal(
                ICustomFieldServiceMax,
                (await service.GetByEntityTypeAsync(templateId, BusinessEntityType.Order)).Count);
        });
    }

    [Fact]
    public async Task Bound_is_independent_per_entity_type()
    {
        await WithFactoryAsync(async factory =>
        {
            var service = new CommerceCustomFieldService(new CustomFieldDefinitionRepository(factory));
            Guid templateId = Guid.NewGuid();

            for (int i = 0; i < ICustomFieldServiceMax; i++)
            {
                await service.AddDefinitionAsync(NewDefinition(templateId, BusinessEntityType.Order, $"order_{i}"));
            }

            // A different entity type starts fresh and still admits a full complement.
            CustomFieldDefinitionResult product = await service.AddDefinitionAsync(
                NewDefinition(templateId, BusinessEntityType.Product, "product_0"));

            Assert.True(product.IsAdded, product.Error);
            Assert.Single(await service.GetByEntityTypeAsync(templateId, BusinessEntityType.Product));
        });
    }

    [Fact]
    public async Task Bound_is_independent_per_template()
    {
        await WithFactoryAsync(async factory =>
        {
            var service = new CommerceCustomFieldService(new CustomFieldDefinitionRepository(factory));
            Guid templateA = Guid.NewGuid();
            Guid templateB = Guid.NewGuid();

            for (int i = 0; i < ICustomFieldServiceMax; i++)
            {
                await service.AddDefinitionAsync(NewDefinition(templateA, BusinessEntityType.Order, $"a_{i}"));
            }

            CustomFieldDefinitionResult onB = await service.AddDefinitionAsync(
                NewDefinition(templateB, BusinessEntityType.Order, "b_0"));

            Assert.True(onB.IsAdded, onB.Error);
            Assert.Single(await service.GetByEntityTypeAsync(templateB, BusinessEntityType.Order));
        });
    }

    [Fact]
    public async Task Soft_delete_frees_a_slot()
    {
        await WithFactoryAsync(async factory =>
        {
            var service = new CommerceCustomFieldService(new CustomFieldDefinitionRepository(factory));
            Guid templateId = Guid.NewGuid();
            var ids = new List<Guid>();

            for (int i = 0; i < ICustomFieldServiceMax; i++)
            {
                CustomFieldDefinitionResult r = await service.AddDefinitionAsync(
                    NewDefinition(templateId, BusinessEntityType.Order, $"field_{i}"));
                ids.Add(r.Definition!.Id);
            }

            await service.DeleteAsync(ids[0]);

            CustomFieldDefinitionResult result = await service.AddDefinitionAsync(
                NewDefinition(templateId, BusinessEntityType.Order, "field_new"));

            Assert.True(result.IsAdded, result.Error);
        });
    }

    [Fact]
    public async Task Add_read_update_delete_round_trips()
    {
        await WithFactoryAsync(async factory =>
        {
            var service = new CommerceCustomFieldService(new CustomFieldDefinitionRepository(factory));
            Guid templateId = Guid.NewGuid();

            CustomFieldDefinitionResult added = await service.AddDefinitionAsync(
                NewDefinition(templateId, BusinessEntityType.Customer, "loyalty"));
            Assert.True(added.IsAdded);
            Guid id = added.Definition!.Id;

            CustomFieldDefinition? fetched = await service.GetByIdAsync(id);
            Assert.NotNull(fetched);
            Assert.Equal(BusinessEntityType.Customer, fetched!.TargetEntityType);

            fetched.DisplayName = "会员等级";
            await service.UpdateAsync(fetched);
            Assert.Equal("会员等级", (await service.GetByIdAsync(id))!.DisplayName);

            Assert.Single(await service.GetByTemplateAsync(templateId));

            await service.DeleteAsync(id);
            Assert.Null(await service.GetByIdAsync(id));
            Assert.Empty(await service.GetByTemplateAsync(templateId));
        });
    }

    [Fact]
    public async Task Add_null_throws()
    {
        await WithFactoryAsync(async factory =>
        {
            var service = new CommerceCustomFieldService(new CustomFieldDefinitionRepository(factory));
            await Assert.ThrowsAsync<ArgumentNullException>(() => service.AddDefinitionAsync(null!));
        });
    }

    private const int ICustomFieldServiceMax = ICustomFieldService.MaxDefinitionsPerEntityType;

    private static CustomFieldDefinition NewDefinition(Guid templateId, BusinessEntityType entityType, string key) => new()
    {
        Id = Guid.NewGuid(),
        TemplateId = templateId,
        TargetEntityType = entityType,
        DataType = CustomFieldDataType.Text,
        FieldKey = key,
        DisplayName = key,
    };

    private static async Task WithFactoryAsync(Func<SqliteConnectionFactory, Task> body)
    {
        string path = Path.Combine(Path.GetTempPath(), $"orderly-customfield-{Guid.NewGuid():N}.db");
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
