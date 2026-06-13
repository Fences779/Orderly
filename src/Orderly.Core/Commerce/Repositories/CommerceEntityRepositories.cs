namespace Orderly.Core.Commerce.Repositories;

/// <summary>
/// One repository interface per Universal_Domain_Model entity (Requirement 3.2). Each interface is
/// a thin, entity-typed specialization of <see cref="ICommerceRepository{TEntity}"/> so callers can
/// depend on a specific entity's repository while sharing the common CRUD contract. There are 18
/// repositories, one per entity in the Universal_Domain_Model.
/// </summary>
file static class CommerceEntityRepositoriesDoc { }

// --- System / configuration entities ---

/// <summary>Repository for <see cref="BusinessWorkspace"/> records.</summary>
public interface IBusinessWorkspaceRepository : ICommerceRepository<BusinessWorkspace> { }

/// <summary>Repository for <see cref="BusinessTemplate"/> records.</summary>
public interface IBusinessTemplateRepository : ICommerceRepository<BusinessTemplate> { }

/// <summary>Repository for <see cref="CustomFieldDefinition"/> records.</summary>
public interface ICustomFieldDefinitionRepository : ICommerceRepository<CustomFieldDefinition> { }

/// <summary>Repository for <see cref="UnitDefinition"/> records.</summary>
public interface IUnitDefinitionRepository : ICommerceRepository<UnitDefinition> { }

// --- Workspace-scoped business entities ---

/// <summary>Repository for <see cref="Product"/> records.</summary>
public interface IProductRepository : ICommerceRepository<Product> { }

/// <summary>Repository for <see cref="ProductVariant"/> records.</summary>
public interface IProductVariantRepository : ICommerceRepository<ProductVariant> { }

/// <summary>Repository for <see cref="InventoryItem"/> records.</summary>
public interface IInventoryItemRepository : ICommerceRepository<InventoryItem> { }

/// <summary>Repository for <see cref="InventoryMovement"/> records.</summary>
public interface IInventoryMovementRepository : ICommerceRepository<InventoryMovement> { }

/// <summary>Repository for <see cref="Customer"/> records.</summary>
public interface ICommerceCustomerRepository : ICommerceRepository<Customer> { }

/// <summary>Repository for <see cref="CustomerContact"/> records.</summary>
public interface ICustomerContactRepository : ICommerceRepository<CustomerContact> { }

/// <summary>Repository for <see cref="Order"/> records.</summary>
public interface ICommerceOrderRepository : ICommerceRepository<Order> { }

/// <summary>Repository for <see cref="OrderItem"/> records.</summary>
public interface IOrderItemRepository : ICommerceRepository<OrderItem> { }

/// <summary>Repository for <see cref="PaymentRecord"/> records.</summary>
public interface IPaymentRecordRepository : ICommerceRepository<PaymentRecord> { }

/// <summary>Repository for <see cref="CashFlowEntry"/> records.</summary>
public interface ICashFlowEntryRepository : ICommerceRepository<CashFlowEntry> { }

/// <summary>Repository for <see cref="Supplier"/> records.</summary>
public interface ISupplierRepository : ICommerceRepository<Supplier> { }

/// <summary>Repository for <see cref="BusinessTask"/> records.</summary>
public interface IBusinessTaskRepository : ICommerceRepository<BusinessTask> { }

/// <summary>Repository for <see cref="BusinessInsight"/> records.</summary>
public interface IBusinessInsightRepository : ICommerceRepository<BusinessInsight> { }

/// <summary>Repository for <see cref="BusinessMetricSnapshot"/> records.</summary>
public interface IBusinessMetricSnapshotRepository : ICommerceRepository<BusinessMetricSnapshot> { }
