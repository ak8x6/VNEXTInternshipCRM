using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using VNEXT.CRM.Plugins.Core;
using VNEXT.CRM.Plugins.Guards;
using Xunit;

namespace VNEXT.CRM.Plugins.Tests
{
    internal sealed class FakeTracingService : ITracingService
    {
        public List<string> Traces { get; } = new List<string>();

        public void Trace(string format, params object[] args)
        {
            Traces.Add(args != null && args.Length > 0 ? string.Format(format, args) : format);
        }
    }

    internal sealed class FakeServiceProvider : IServiceProvider
    {
        private readonly IPluginExecutionContext _context;
        private readonly IOrganizationServiceFactory _factory;
        private readonly ITracingService _tracing;

        public FakeServiceProvider(
            IPluginExecutionContext context,
            IOrganizationServiceFactory factory,
            ITracingService tracing = null)
        {
            _context = context;
            _factory = factory;
            _tracing = tracing ?? new FakeTracingService();
        }

        public object GetService(Type serviceType)
        {
            if (serviceType == typeof(IPluginExecutionContext))
            {
                return _context;
            }

            if (serviceType == typeof(IOrganizationServiceFactory))
            {
                return _factory;
            }

            if (serviceType == typeof(ITracingService))
            {
                return _tracing;
            }

            return null;
        }
    }

    public class MasterDataGuardTests
    {
        private static readonly Guid CorrelationId = new Guid("12121212-1212-1212-1212-121212121212");
        private static readonly Guid CallerUserId = new Guid("23232323-2323-2323-2323-232323232323");
        private static readonly Guid WriterUserId = new Guid("34343434-3434-3434-3434-343434343434");
        private static readonly Guid BaseCurrencyId = new Guid("45454545-4545-4545-4545-454545454545");
        private static readonly Guid ForeignCurrencyId = new Guid("56565656-5656-5656-5656-565656565656");
        private static readonly Guid AccountAId = new Guid("67676767-6767-6767-6767-676767676767");
        private static readonly Guid AccountBId = new Guid("78787878-7878-7878-7878-787878787878");
        private static readonly Guid ContactAId = new Guid("89898989-8989-8989-8989-898989898989");
        private static readonly Guid ProductId = new Guid("90909090-9090-9090-9090-909090909090");

        private static void AssertCrmError(string expectedCode, Action action)
        {
            var ex = Assert.Throws<InvalidPluginExecutionException>(action);
            Assert.StartsWith("[" + expectedCode + "] ", ex.Message);
            Assert.Contains("(CorrelationId: " + CorrelationId.ToString("D") + ")", ex.Message);
        }

        private static EntityCollection BuildPolicyEnvVarsResult()
        {
            var def = new Entity(CrmSchema.Entities.EnvironmentVariableDefinition, Guid.NewGuid());
            def[CrmSchema.Platform.EnvVarSchemaName] = CrmSchema.EnvVars.InternalWriterUserId;
            def["val." + CrmSchema.Platform.EnvVarValue] = new AliasedValue(
                CrmSchema.Entities.EnvironmentVariableValue,
                CrmSchema.Platform.EnvVarValue,
                WriterUserId.ToString("D"));
            return new EntityCollection(new[] { def });
        }

        private static EntityCollection BuildOrganizationBaseCurrencyResult()
        {
            var org = new Entity(CrmSchema.Entities.Organization, Guid.NewGuid());
            org[CrmSchema.Platform.OrganizationBaseCurrencyId] = new EntityReference(CrmSchema.Entities.TransactionCurrency, BaseCurrencyId);
            return new EntityCollection(new[] { org });
        }

        private static FakeOrganizationService CreateBaseCallerService(
            Func<string, Guid, ColumnSet, Entity> onRetrieve = null,
            Func<QueryExpression, EntityCollection> onQuery = null)
        {
            return new FakeOrganizationService
            {
                OnRetrieve = onRetrieve,
                OnRetrieveMultiple = q =>
                {
                    var qe = q as QueryExpression;
                    if (qe != null)
                    {
                        if (qe.EntityName == CrmSchema.Entities.EnvironmentVariableDefinition)
                        {
                            return BuildPolicyEnvVarsResult();
                        }

                        if (qe.EntityName == CrmSchema.Entities.Organization)
                        {
                            return BuildOrganizationBaseCurrencyResult();
                        }

                        if (onQuery != null)
                        {
                            return onQuery(qe);
                        }
                    }

                    return new EntityCollection();
                }
            };
        }

        private static FakeOrganizationService CreateBaseWriterService(
            Func<QueryExpression, EntityCollection> onQuery = null)
        {
            return new FakeOrganizationService
            {
                OnRetrieve = (entityName, id, cols) =>
                {
                    if (entityName == CrmSchema.Entities.SystemUser && id == WriterUserId)
                    {
                        var user = new Entity(CrmSchema.Entities.SystemUser, WriterUserId);
                        user[CrmSchema.Platform.IsDisabled] = false;
                        user[CrmSchema.Platform.BusinessUnitId] = new EntityReference(CrmSchema.Entities.BusinessUnit, Guid.NewGuid());
                        return user;
                    }

                    return null;
                },
                OnRetrieveMultiple = q =>
                {
                    var qe = q as QueryExpression;
                    if (qe != null && onQuery != null)
                    {
                        return onQuery(qe);
                    }

                    return new EntityCollection();
                }
            };
        }

        private static FakePluginExecutionContext CreateContext(string entityName, string messageName, ParameterCollection inputs)
        {
            return new FakePluginExecutionContext
            {
                PrimaryEntityName = entityName,
                MessageName = messageName,
                Stage = 20,
                Mode = 0,
                IsInTransaction = true,
                InitiatingUserId = CallerUserId,
                UserId = CallerUserId,
                CorrelationId = CorrelationId,
                InputParameters = inputs ?? new ParameterCollection()
            };
        }

        private static void RunPlugin(
            IPluginExecutionContext context,
            IOrganizationService callerService,
            IOrganizationService writerService = null)
        {
            var effectiveWriter = writerService ?? CreateBaseWriterService();
            var factory = new FakeOrganizationServiceFactory(uid =>
                uid == CallerUserId ? callerService :
                uid == WriterUserId ? effectiveWriter : null);

            var plugin = new MasterDataValidationPlugin();
            plugin.Execute(new FakeServiceProvider(context, factory));
        }

        [Fact]
        public void Account_CreateAndUpdate_DefaultsTypeValidatesPrimaryContactAndBlocksActiveDealDeactivation()
        {
            // Valid Create defaults vn_accounttype to Prospect
            var createTarget = new Entity(CrmSchema.Entities.Account, AccountAId);
            createTarget[CrmSchema.Account.Name] = "Acme Industrial";
            var createCtx = CreateContext(
                CrmSchema.Entities.Account,
                "Create",
                new ParameterCollection { { "Target", createTarget } });
            RunPlugin(createCtx, CreateBaseCallerService());
            Assert.Equal(
                CrmSchema.Account.AccountTypeValues.Prospect,
                createTarget.GetAttributeValue<OptionSetValue>(CrmSchema.Account.AccountType).Value);

            // Update primarycontactid -> matching active Contact passes
            var existingAccount = new Entity(CrmSchema.Entities.Account, AccountAId);
            existingAccount[CrmSchema.Account.Name] = "Acme Industrial";
            existingAccount[CrmSchema.Account.AccountType] = new OptionSetValue(CrmSchema.Account.AccountTypeValues.Customer);

            var matchingContact = new Entity(CrmSchema.Entities.Contact, ContactAId);
            matchingContact[CrmSchema.Contact.StateCode] = new OptionSetValue(CrmSchema.Contact.State.Active);
            matchingContact[CrmSchema.Contact.ParentCustomerId] = new EntityReference(CrmSchema.Entities.Account, AccountAId);

            var validPrimaryUpdate = new Entity(CrmSchema.Entities.Account, AccountAId);
            validPrimaryUpdate[CrmSchema.Account.PrimaryContactId] = new EntityReference(CrmSchema.Entities.Contact, ContactAId);
            var validUpdateCtx = CreateContext(
                CrmSchema.Entities.Account,
                "Update",
                new ParameterCollection { { "Target", validPrimaryUpdate } });
            validUpdateCtx.PreEntityImages["PreImage"] = existingAccount;

            var callerForMatching = CreateBaseCallerService(onRetrieve: (e, id, c) => e == CrmSchema.Entities.Contact && id == ContactAId ? matchingContact : null);
            RunPlugin(validUpdateCtx, callerForMatching);

            // Update primarycontactid -> Contact belonging to another Account fails with CRM004
            var wrongAccountContact = new Entity(CrmSchema.Entities.Contact, ContactAId);
            wrongAccountContact[CrmSchema.Contact.StateCode] = new OptionSetValue(CrmSchema.Contact.State.Active);
            wrongAccountContact[CrmSchema.Contact.ParentCustomerId] = new EntityReference(CrmSchema.Entities.Account, AccountBId);
            var callerForWrongParent = CreateBaseCallerService(onRetrieve: (e, id, c) => e == CrmSchema.Entities.Contact ? wrongAccountContact : null);
            AssertCrmError(CrmError.CRM004, () => RunPlugin(validUpdateCtx, callerForWrongParent));

            // Update primarycontactid -> Inactive Contact fails with CRM004
            var inactiveContact = new Entity(CrmSchema.Entities.Contact, ContactAId);
            inactiveContact[CrmSchema.Contact.StateCode] = new OptionSetValue(CrmSchema.Contact.State.Inactive);
            inactiveContact[CrmSchema.Contact.ParentCustomerId] = new EntityReference(CrmSchema.Entities.Account, AccountAId);
            var callerForInactiveContact = CreateBaseCallerService(onRetrieve: (e, id, c) => e == CrmSchema.Entities.Contact ? inactiveContact : null);
            AssertCrmError(CrmError.CRM004, () => RunPlugin(validUpdateCtx, callerForInactiveContact));

            // Deactivation via Update with active Open Opportunity -> CRM017
            var deactivateTarget = new Entity(CrmSchema.Entities.Account, AccountAId);
            deactivateTarget[CrmSchema.Account.StateCode] = new OptionSetValue(CrmSchema.Account.State.Inactive);
            var deactivateUpdateCtx = CreateContext(
                CrmSchema.Entities.Account,
                "Update",
                new ParameterCollection { { "Target", deactivateTarget } });
            deactivateUpdateCtx.PreEntityImages["PreImage"] = existingAccount;

            var writerWithOpenOpp = CreateBaseWriterService(onQuery: qe =>
                qe.EntityName == CrmSchema.Entities.Opportunity
                    ? new EntityCollection(new[] { new Entity(CrmSchema.Entities.Opportunity, Guid.NewGuid()) })
                    : new EntityCollection());

            AssertCrmError(CrmError.CRM017, () => RunPlugin(deactivateUpdateCtx, CreateBaseCallerService(), writerWithOpenOpp));

            // Deactivation via SetState with active Open Opportunity -> CRM017
            var setStateCtx = CreateContext(
                CrmSchema.Entities.Account,
                "SetState",
                new ParameterCollection
                {
                    { "EntityMoniker", new EntityReference(CrmSchema.Entities.Account, AccountAId) },
                    { "State", new OptionSetValue(CrmSchema.Account.State.Inactive) }
                });
            AssertCrmError(CrmError.CRM017, () => RunPlugin(setStateCtx, CreateBaseCallerService(), writerWithOpenOpp));

            // Deactivation without active Open Opportunity -> passes
            RunPlugin(deactivateUpdateCtx, CreateBaseCallerService(), CreateBaseWriterService());
            RunPlugin(setStateCtx, CreateBaseCallerService(), CreateBaseWriterService());

            // Delete of referenced Account -> CRM017
            var deleteCtx = CreateContext(
                CrmSchema.Entities.Account,
                "Delete",
                new ParameterCollection { { "Target", new EntityReference(CrmSchema.Entities.Account, AccountAId) } });
            AssertCrmError(CrmError.CRM017, () => RunPlugin(deleteCtx, CreateBaseCallerService(), writerWithOpenOpp));

            // Merge on Account -> always CRM017
            var mergeCtx = CreateContext(
                CrmSchema.Entities.Account,
                "Merge",
                new ParameterCollection { { "Target", new EntityReference(CrmSchema.Entities.Account, AccountAId) } });
            AssertCrmError(CrmError.CRM017, () => RunPlugin(mergeCtx, CreateBaseCallerService()));
        }

        [Fact]
        public void Contact_CreateUpdateDeactivateAndMerge_EnforceParentImmutabilityContactabilityAndProtection()
        {
            var activeAccount = new Entity(CrmSchema.Entities.Account, AccountAId);
            activeAccount[CrmSchema.Account.StateCode] = new OptionSetValue(CrmSchema.Account.State.Active);

            var inactiveAccount = new Entity(CrmSchema.Entities.Account, AccountBId);
            inactiveAccount[CrmSchema.Account.StateCode] = new OptionSetValue(CrmSchema.Account.State.Inactive);

            var existingContact = new Entity(CrmSchema.Entities.Contact, ContactAId);
            existingContact[CrmSchema.Contact.LastName] = "Al-Mansoor";
            existingContact[CrmSchema.Contact.ParentCustomerId] = new EntityReference(CrmSchema.Entities.Account, AccountAId);
            existingContact[CrmSchema.Contact.EmailAddress1] = "buyer@acme.example";
            existingContact[CrmSchema.Contact.PreferredMethod] = new OptionSetValue(CrmSchema.Contact.PreferredMethodValues.Email);
            existingContact[CrmSchema.Contact.StateCode] = new OptionSetValue(CrmSchema.Contact.State.Active);

            var caller = CreateBaseCallerService(onRetrieve: (entityName, id, cols) =>
            {
                if (entityName == CrmSchema.Entities.Account && id == AccountAId) return activeAccount;
                if (entityName == CrmSchema.Entities.Account && id == AccountBId) return inactiveAccount;
                if (entityName == CrmSchema.Entities.Contact && id == ContactAId) return existingContact;
                return null;
            });

            // Valid phone-only Create defaults vn_preferredmethod = Phone and vn_donotcontact = false
            var phoneOnlyContact = new Entity(CrmSchema.Entities.Contact, Guid.NewGuid());
            phoneOnlyContact[CrmSchema.Contact.LastName] = "Hassan";
            phoneOnlyContact[CrmSchema.Contact.ParentCustomerId] = new EntityReference(CrmSchema.Entities.Account, AccountAId);
            phoneOnlyContact[CrmSchema.Contact.MobilePhone] = "+1-555-0101";

            var createCtx = CreateContext(
                CrmSchema.Entities.Contact,
                "Create",
                new ParameterCollection { { "Target", phoneOnlyContact } });
            RunPlugin(createCtx, caller);
            Assert.Equal(
                CrmSchema.Contact.PreferredMethodValues.Phone,
                phoneOnlyContact.GetAttributeValue<OptionSetValue>(CrmSchema.Contact.PreferredMethod).Value);
            Assert.False(phoneOnlyContact.GetAttributeValue<bool>(CrmSchema.Contact.DoNotContact));

            // Non-Account parentcustomerid -> CRM004
            var wrongTypeParent = new Entity(CrmSchema.Entities.Contact, Guid.NewGuid());
            wrongTypeParent[CrmSchema.Contact.LastName] = "Hassan";
            wrongTypeParent[CrmSchema.Contact.ParentCustomerId] = new EntityReference(CrmSchema.Entities.Contact, ContactAId);
            wrongTypeParent[CrmSchema.Contact.MobilePhone] = "+1-555-0101";
            AssertCrmError(CrmError.CRM004, () => RunPlugin(
                CreateContext(CrmSchema.Entities.Contact, "Create", new ParameterCollection { { "Target", wrongTypeParent } }),
                caller));

            // Inactive parent Account -> CRM004
            var inactiveParentContact = new Entity(CrmSchema.Entities.Contact, Guid.NewGuid());
            inactiveParentContact[CrmSchema.Contact.LastName] = "Hassan";
            inactiveParentContact[CrmSchema.Contact.ParentCustomerId] = new EntityReference(CrmSchema.Entities.Account, AccountBId);
            inactiveParentContact[CrmSchema.Contact.MobilePhone] = "+1-555-0101";
            AssertCrmError(CrmError.CRM004, () => RunPlugin(
                CreateContext(CrmSchema.Entities.Contact, "Create", new ParameterCollection { { "Target", inactiveParentContact } }),
                caller));

            // Reparenting parentcustomerid on single Update -> CRM004
            var reparentTarget = new Entity(CrmSchema.Entities.Contact, ContactAId);
            reparentTarget[CrmSchema.Contact.ParentCustomerId] = new EntityReference(CrmSchema.Entities.Account, AccountBId);
            var reparentCtx = CreateContext(
                CrmSchema.Entities.Contact,
                "Update",
                new ParameterCollection { { "Target", reparentTarget } });
            reparentCtx.PreEntityImages["PreImage"] = existingContact;
            AssertCrmError(CrmError.CRM004, () => RunPlugin(reparentCtx, caller));

            // Reparenting parentcustomerid on UpdateMultiple -> CRM004
            var updateMultipleCtx = CreateContext(
                CrmSchema.Entities.Contact,
                "UpdateMultiple",
                new ParameterCollection { { "Targets", new EntityCollection(new[] { reparentTarget }) } });
            AssertCrmError(CrmError.CRM004, () => RunPlugin(updateMultipleCtx, caller));

            // Invalid email / preferred-method mismatch -> CRM001
            var badPrefTarget = new Entity(CrmSchema.Entities.Contact, ContactAId);
            badPrefTarget[CrmSchema.Contact.PreferredMethod] = new OptionSetValue(CrmSchema.Contact.PreferredMethodValues.Phone);
            var badPrefCtx = CreateContext(
                CrmSchema.Entities.Contact,
                "Update",
                new ParameterCollection { { "Target", badPrefTarget } });
            badPrefCtx.PreEntityImages["PreImage"] = existingContact;
            AssertCrmError(CrmError.CRM001, () => RunPlugin(badPrefCtx, caller));

            // Deactivation while used on Open Opportunity -> CRM017
            var deactivateContactTarget = new Entity(CrmSchema.Entities.Contact, ContactAId);
            deactivateContactTarget[CrmSchema.Contact.StateCode] = new OptionSetValue(CrmSchema.Contact.State.Inactive);
            var deactivateContactCtx = CreateContext(
                CrmSchema.Entities.Contact,
                "Update",
                new ParameterCollection { { "Target", deactivateContactTarget } });
            deactivateContactCtx.PreEntityImages["PreImage"] = existingContact;
            var writerWithOpenOpp = CreateBaseWriterService(onQuery: qe =>
                qe.EntityName == CrmSchema.Entities.Opportunity
                    ? new EntityCollection(new[] { new Entity(CrmSchema.Entities.Opportunity, Guid.NewGuid()) })
                    : new EntityCollection());
            AssertCrmError(CrmError.CRM017, () => RunPlugin(deactivateContactCtx, caller, writerWithOpenOpp));

            // Merge on Contact -> always CRM017
            var mergeContactCtx = CreateContext(
                CrmSchema.Entities.Contact,
                "Merge",
                new ParameterCollection { { "Target", new EntityReference(CrmSchema.Entities.Contact, ContactAId) } });
            AssertCrmError(CrmError.CRM017, () => RunPlugin(mergeContactCtx, caller));
        }

        [Fact]
        public void Product_CreateUpdateAndDelete_EnforcesCurrencySkuPriceAndInUseProtections()
        {
            // Valid Create auto-populates base transactioncurrencyid
            var createProduct = new Entity(CrmSchema.Entities.Product, ProductId);
            createProduct[CrmSchema.Product.Name] = "Enterprise License";
            createProduct[CrmSchema.Product.Sku] = "CRM-ENT-01";
            createProduct[CrmSchema.Product.Description] = "Annual enterprise CRM subscription.";
            createProduct[CrmSchema.Product.UnitPrice] = new Money(1250.5000m);

            var createCtx = CreateContext(
                CrmSchema.Entities.Product,
                "Create",
                new ParameterCollection { { "Target", createProduct } });
            RunPlugin(createCtx, CreateBaseCallerService());
            Assert.Equal(
                BaseCurrencyId,
                createProduct.GetAttributeValue<EntityReference>(CrmSchema.Product.TransactionCurrencyId).Id);

            // Invalid SKU / price / foreign currency -> CRM008
            var badSkuProduct = new Entity(CrmSchema.Entities.Product, Guid.NewGuid());
            badSkuProduct[CrmSchema.Product.Name] = "Bad SKU";
            badSkuProduct[CrmSchema.Product.Sku] = "bad_sku";
            badSkuProduct[CrmSchema.Product.Description] = "Desc";
            badSkuProduct[CrmSchema.Product.UnitPrice] = new Money(100m);
            AssertCrmError(CrmError.CRM008, () => RunPlugin(
                CreateContext(CrmSchema.Entities.Product, "Create", new ParameterCollection { { "Target", badSkuProduct } }),
                CreateBaseCallerService()));

            var foreignCurrencyProduct = new Entity(CrmSchema.Entities.Product, Guid.NewGuid());
            foreignCurrencyProduct[CrmSchema.Product.Name] = "Foreign";
            foreignCurrencyProduct[CrmSchema.Product.Sku] = "CRM-EUR-01";
            foreignCurrencyProduct[CrmSchema.Product.Description] = "Desc";
            foreignCurrencyProduct[CrmSchema.Product.UnitPrice] = new Money(100m);
            foreignCurrencyProduct[CrmSchema.Product.TransactionCurrencyId] = new EntityReference(CrmSchema.Entities.TransactionCurrency, ForeignCurrencyId);
            AssertCrmError(CrmError.CRM008, () => RunPlugin(
                CreateContext(CrmSchema.Entities.Product, "Create", new ParameterCollection { { "Target", foreignCurrencyProduct } }),
                CreateBaseCallerService()));

            var existingProduct = new Entity(CrmSchema.Entities.Product, ProductId);
            existingProduct[CrmSchema.Product.Name] = "Enterprise License";
            existingProduct[CrmSchema.Product.Sku] = "CRM-ENT-01";
            existingProduct[CrmSchema.Product.Description] = "Annual enterprise CRM subscription.";
            existingProduct[CrmSchema.Product.UnitPrice] = new Money(1250.5000m);
            existingProduct[CrmSchema.Product.TransactionCurrencyId] = new EntityReference(CrmSchema.Entities.TransactionCurrency, BaseCurrencyId);

            var callerUnused = CreateBaseCallerService(
                onRetrieve: (e, id, c) => e == CrmSchema.Entities.Product && id == ProductId ? existingProduct : null,
                onQuery: qe => new EntityCollection());

            var callerInUse = CreateBaseCallerService(
                onRetrieve: (e, id, c) => e == CrmSchema.Entities.Product && id == ProductId ? existingProduct : null,
                onQuery: qe => qe.EntityName == CrmSchema.Entities.OpportunityLine
                    ? new EntityCollection(new[] { new Entity(CrmSchema.Entities.OpportunityLine, Guid.NewGuid()) })
                    : new EntityCollection());

            // Price update on in-use Product -> ALLOWED (existing lines keep immutable snapshots)
            var priceUpdate = new Entity(CrmSchema.Entities.Product, ProductId);
            priceUpdate[CrmSchema.Product.UnitPrice] = new Money(1500.0000m);
            var priceUpdateCtx = CreateContext(
                CrmSchema.Entities.Product,
                "Update",
                new ParameterCollection { { "Target", priceUpdate } });
            priceUpdateCtx.PreEntityImages["PreImage"] = existingProduct;
            RunPlugin(priceUpdateCtx, callerInUse);

            // SKU change on unused Product -> ALLOWED
            var skuUpdate = new Entity(CrmSchema.Entities.Product, ProductId);
            skuUpdate[CrmSchema.Product.Sku] = "CRM-ENT-02";
            var skuUpdateCtx = CreateContext(
                CrmSchema.Entities.Product,
                "Update",
                new ParameterCollection { { "Target", skuUpdate } });
            skuUpdateCtx.PreEntityImages["PreImage"] = existingProduct;
            RunPlugin(skuUpdateCtx, callerUnused);

            // SKU change on in-use Product -> CRM010
            AssertCrmError(CrmError.CRM010, () => RunPlugin(skuUpdateCtx, callerInUse));

            // Delete on in-use Product -> CRM017
            var deleteCtx = CreateContext(
                CrmSchema.Entities.Product,
                "Delete",
                new ParameterCollection { { "Target", new EntityReference(CrmSchema.Entities.Product, ProductId) } });
            AssertCrmError(CrmError.CRM017, () => RunPlugin(deleteCtx, callerInUse));

            // DeleteMultiple on in-use Product -> CRM017
            var deleteMultipleCtx = CreateContext(
                CrmSchema.Entities.Product,
                "DeleteMultiple",
                new ParameterCollection
                {
                    { "Targets", new EntityReferenceCollection { new EntityReference(CrmSchema.Entities.Product, ProductId) } }
                });
            AssertCrmError(CrmError.CRM017, () => RunPlugin(deleteMultipleCtx, callerInUse));
        }
    }
}
