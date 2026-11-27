using System;
using System.ServiceModel;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using VNEXT.CRM.Plugins.Core;
using VNEXT.CRM.Plugins.Domain;
using VNEXT.CRM.Plugins.Services;

namespace VNEXT.CRM.Plugins.Guards
{
    public sealed class MasterDataValidationPlugin : IPlugin
    {
        private const string PreImageName = "PreImage";

        private static readonly string[] AccountColumns =
        {
            CrmSchema.Account.Name,
            CrmSchema.Account.AccountType,
            CrmSchema.Account.Industry,
            CrmSchema.Account.PrimaryContactId,
            CrmSchema.Account.StateCode,
            CrmSchema.Account.TransactionCurrencyId
        };

        private static readonly string[] ContactColumns =
        {
            CrmSchema.Contact.LastName,
            CrmSchema.Contact.ParentCustomerId,
            CrmSchema.Contact.EmailAddress1,
            CrmSchema.Contact.MobilePhone,
            CrmSchema.Contact.PreferredMethod,
            CrmSchema.Contact.StateCode,
            CrmSchema.Contact.TransactionCurrencyId
        };

        private static readonly string[] ProductColumns =
        {
            CrmSchema.Product.Name,
            CrmSchema.Product.Sku,
            CrmSchema.Product.Description,
            CrmSchema.Product.UnitPrice,
            CrmSchema.Product.TransactionCurrencyId
        };

        public void Execute(IServiceProvider serviceProvider)
        {
            if (serviceProvider == null)
            {
                CrmError.Throw(CrmError.CRM008, "Plugin service provider is unavailable.", Guid.Empty);
            }

            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            if (context == null || factory == null)
            {
                CrmError.Throw(CrmError.CRM008, "Plugin execution context or service factory is unavailable.", Guid.Empty);
            }

            var correlationId = context.CorrelationId;
            var entityName = context.PrimaryEntityName ?? string.Empty;
            var message = context.MessageName ?? string.Empty;

            if (tracing != null)
            {
                tracing.Trace("PL01 MasterDataValidationPlugin: Message={0}, Entity={1}, Stage={2}, CorrelationId={3:D}", message, entityName, context.Stage, correlationId);
            }

            if (string.Equals(message, "Merge", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(entityName, CrmSchema.Entities.Account, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(entityName, CrmSchema.Entities.Contact, StringComparison.OrdinalIgnoreCase))
                {
                    CrmError.Throw(
                        CrmError.CRM017,
                        "Customer record merging is not permitted; protected customer history must be retained.",
                        correlationId);
                }

                return;
            }

            var callerService = AuthorizationService.CreateCallerService(factory, context);
            var config = CrmPolicyConfigLoader.Load(callerService, correlationId);

            IOrganizationService cachedWriterService = null;
            Func<IOrganizationService> getWriterService = () =>
            {
                if (cachedWriterService == null)
                {
                    cachedWriterService = AuthorizationService.CreateInternalWriterService(factory, config, correlationId);
                }

                return cachedWriterService;
            };

            if (string.Equals(entityName, CrmSchema.Entities.Account, StringComparison.OrdinalIgnoreCase))
            {
                HandleAccount(context, message, callerService, getWriterService, config, correlationId);
            }
            else if (string.Equals(entityName, CrmSchema.Entities.Contact, StringComparison.OrdinalIgnoreCase))
            {
                HandleContact(context, message, callerService, getWriterService, config, correlationId);
            }
            else if (string.Equals(entityName, CrmSchema.Entities.Product, StringComparison.OrdinalIgnoreCase))
            {
                HandleProduct(context, message, callerService, config, correlationId);
            }
        }

        private static void HandleAccount(
            IPluginExecutionContext context,
            string message,
            IOrganizationService callerService,
            Func<IOrganizationService> getWriterService,
            CrmPolicyConfig config,
            Guid correlationId)
        {
            if (string.Equals(message, "Create", StringComparison.OrdinalIgnoreCase))
            {
                var target = GetTargetEntity(context);
                if (target != null)
                {
                    ValidateAccountCreate(target, callerService, config, correlationId);
                }
            }
            else if (string.Equals(message, "CreateMultiple", StringComparison.OrdinalIgnoreCase))
            {
                var targets = GetTargetEntities(context);
                if (targets != null)
                {
                    foreach (var target in targets.Entities)
                    {
                        ValidateAccountCreate(target, callerService, config, correlationId);
                    }
                }
            }
            else if (string.Equals(message, "Update", StringComparison.OrdinalIgnoreCase))
            {
                var target = GetTargetEntity(context);
                if (target != null)
                {
                    ValidateAccountUpdate(context, target, allowPreImage: true, callerService, getWriterService, config, correlationId);
                }
            }
            else if (string.Equals(message, "UpdateMultiple", StringComparison.OrdinalIgnoreCase))
            {
                var targets = GetTargetEntities(context);
                if (targets != null)
                {
                    foreach (var target in targets.Entities)
                    {
                        ValidateAccountUpdate(context, target, allowPreImage: false, callerService, getWriterService, config, correlationId);
                    }
                }
            }
            else if (IsSetStateMessage(message))
            {
                var moniker = GetEntityMoniker(context);
                var state = GetSetStateValue(context);
                if (moniker != null && state.HasValue && state.Value == CrmSchema.Account.State.Inactive)
                {
                    EnsureAccountNotUsedInActiveOpenOpportunities(moniker.Id, getWriterService(), correlationId);
                }
            }
            else if (string.Equals(message, "Delete", StringComparison.OrdinalIgnoreCase))
            {
                var targetRef = GetTargetReference(context);
                if (targetRef != null)
                {
                    ValidateAccountDelete(targetRef.Id, callerService, getWriterService, correlationId);
                }
            }
            else if (string.Equals(message, "DeleteMultiple", StringComparison.OrdinalIgnoreCase))
            {
                var targets = GetTargetReferences(context);
                if (targets != null)
                {
                    foreach (var targetRef in targets)
                    {
                        ValidateAccountDelete(targetRef.Id, callerService, getWriterService, correlationId);
                    }
                }
            }
        }

        private static void ValidateAccountCreate(
            Entity target,
            IOrganizationService callerService,
            CrmPolicyConfig config,
            Guid correlationId)
        {
            if (!target.Contains(CrmSchema.Account.AccountType) || target[CrmSchema.Account.AccountType] == null)
            {
                target[CrmSchema.Account.AccountType] = new OptionSetValue(CrmSchema.Account.AccountTypeValues.Prospect);
            }

            var name = target.GetAttributeValue<string>(CrmSchema.Account.Name);
            var accountType = GetOptionSetInt(target, CrmSchema.Account.AccountType);
            var industry = GetOptionSetInt(target, CrmSchema.Account.Industry);
            var currencyRef = target.GetAttributeValue<EntityReference>(CrmSchema.Account.TransactionCurrencyId);

            DomainValidators.ValidateAccount(name, accountType, industry, correlationId);
            DomainPricing.ValidateBaseCurrency(currencyRef != null ? (Guid?)currencyRef.Id : null, config, correlationId);

            if (target.Contains(CrmSchema.Account.PrimaryContactId))
            {
                var primaryContactRef = target.GetAttributeValue<EntityReference>(CrmSchema.Account.PrimaryContactId);
                if (primaryContactRef != null)
                {
                    ValidateAccountPrimaryContact(target.Id, primaryContactRef, callerService, correlationId);
                }
            }
        }

        private static void ValidateAccountUpdate(
            IPluginExecutionContext context,
            Entity target,
            bool allowPreImage,
            IOrganizationService callerService,
            Func<IOrganizationService> getWriterService,
            CrmPolicyConfig config,
            Guid correlationId)
        {
            var accountId = target.Id != Guid.Empty ? target.Id : context.PrimaryEntityId;
            var existing = LoadExistingEntity(context, CrmSchema.Entities.Account, accountId, AccountColumns, allowPreImage, callerService, correlationId);
            var merged = MergeOverExisting(CrmSchema.Entities.Account, accountId, existing, target);

            var name = merged.GetAttributeValue<string>(CrmSchema.Account.Name);
            var accountType = GetOptionSetInt(merged, CrmSchema.Account.AccountType);
            var industry = GetOptionSetInt(merged, CrmSchema.Account.Industry);
            var currencyRef = merged.GetAttributeValue<EntityReference>(CrmSchema.Account.TransactionCurrencyId);

            DomainValidators.ValidateAccount(name, accountType, industry, correlationId);
            DomainPricing.ValidateBaseCurrency(currencyRef != null ? (Guid?)currencyRef.Id : null, config, correlationId);

            if (target.Contains(CrmSchema.Account.PrimaryContactId))
            {
                var primaryContactRef = target.GetAttributeValue<EntityReference>(CrmSchema.Account.PrimaryContactId);
                if (primaryContactRef != null)
                {
                    ValidateAccountPrimaryContact(accountId, primaryContactRef, callerService, correlationId);
                }
            }

            if (target.Contains(CrmSchema.Account.StateCode))
            {
                var newState = GetOptionSetInt(target, CrmSchema.Account.StateCode);
                if (newState.HasValue && newState.Value == CrmSchema.Account.State.Inactive)
                {
                    EnsureAccountNotUsedInActiveOpenOpportunities(accountId, getWriterService(), correlationId);
                }
            }
        }

        private static void ValidateAccountPrimaryContact(
            Guid accountId,
            EntityReference primaryContactRef,
            IOrganizationService callerService,
            Guid correlationId)
        {
            if (accountId == Guid.Empty
                || primaryContactRef == null
                || primaryContactRef.Id == Guid.Empty
                || !string.Equals(primaryContactRef.LogicalName, CrmSchema.Entities.Contact, StringComparison.OrdinalIgnoreCase))
            {
                CrmError.Throw(
                    CrmError.CRM004,
                    "Account Primary Contact must be an active Contact belonging to this Account.",
                    correlationId);
            }

            Entity contact;
            try
            {
                contact = callerService.Retrieve(
                    CrmSchema.Entities.Contact,
                    primaryContactRef.Id,
                    new ColumnSet(CrmSchema.Contact.StateCode, CrmSchema.Contact.ParentCustomerId));
            }
            catch (FaultException<OrganizationServiceFault>)
            {
                CrmError.Throw(
                    CrmError.CRM004,
                    "Account Primary Contact must be an active Contact belonging to this Account.",
                    correlationId);
                return;
            }

            if (contact == null)
            {
                CrmError.Throw(
                    CrmError.CRM004,
                    "Account Primary Contact must be an active Contact belonging to this Account.",
                    correlationId);
            }

            var contactState = GetOptionSetInt(contact, CrmSchema.Contact.StateCode);
            var parentRef = contact.GetAttributeValue<EntityReference>(CrmSchema.Contact.ParentCustomerId);
            if (contactState != CrmSchema.Contact.State.Active
                || parentRef == null
                || !string.Equals(parentRef.LogicalName, CrmSchema.Entities.Account, StringComparison.OrdinalIgnoreCase)
                || parentRef.Id != accountId)
            {
                CrmError.Throw(
                    CrmError.CRM004,
                    "Account Primary Contact must be an active Contact belonging to this Account.",
                    correlationId);
            }
        }

        private static void EnsureAccountNotUsedInActiveOpenOpportunities(
            Guid accountId,
            IOrganizationService writerService,
            Guid correlationId)
        {
            if (HasAnyRecord(
                writerService,
                CrmSchema.Entities.Opportunity,
                new ConditionExpression(CrmSchema.Opportunity.Account, ConditionOperator.Equal, accountId),
                new ConditionExpression(CrmSchema.Opportunity.StateCode, ConditionOperator.Equal, CrmSchema.Opportunity.State.Active)))
            {
                CrmError.Throw(
                    CrmError.CRM017,
                    "Cannot deactivate an Account referenced by an active Open Opportunity.",
                    correlationId);
            }
        }

        private static void ValidateAccountDelete(
            Guid accountId,
            IOrganizationService callerService,
            Func<IOrganizationService> getWriterService,
            Guid correlationId)
        {
            if (HasAnyRecord(
                getWriterService(),
                CrmSchema.Entities.Opportunity,
                new ConditionExpression(CrmSchema.Opportunity.Account, ConditionOperator.Equal, accountId)))
            {
                CrmError.Throw(
                    CrmError.CRM017,
                    "Account cannot be deleted because it is referenced by existing Opportunities.",
                    correlationId);
            }

            if (HasAnyRecord(
                callerService,
                CrmSchema.Entities.Contact,
                new ConditionExpression(CrmSchema.Contact.ParentCustomerId, ConditionOperator.Equal, accountId)))
            {
                CrmError.Throw(
                    CrmError.CRM017,
                    "Account cannot be deleted because it has associated Contacts.",
                    correlationId);
            }

            if (HasAnyRecord(
                callerService,
                CrmSchema.Entities.Lead,
                new ConditionExpression(CrmSchema.Lead.Account, ConditionOperator.Equal, accountId)))
            {
                CrmError.Throw(
                    CrmError.CRM017,
                    "Account cannot be deleted because it is referenced by historical Leads.",
                    correlationId);
            }
        }

        private static void HandleContact(
            IPluginExecutionContext context,
            string message,
            IOrganizationService callerService,
            Func<IOrganizationService> getWriterService,
            CrmPolicyConfig config,
            Guid correlationId)
        {
            if (string.Equals(message, "Create", StringComparison.OrdinalIgnoreCase))
            {
                var target = GetTargetEntity(context);
                if (target != null)
                {
                    ValidateContactCreate(target, callerService, config, correlationId);
                }
            }
            else if (string.Equals(message, "CreateMultiple", StringComparison.OrdinalIgnoreCase))
            {
                var targets = GetTargetEntities(context);
                if (targets != null)
                {
                    foreach (var target in targets.Entities)
                    {
                        ValidateContactCreate(target, callerService, config, correlationId);
                    }
                }
            }
            else if (string.Equals(message, "Update", StringComparison.OrdinalIgnoreCase))
            {
                var target = GetTargetEntity(context);
                if (target != null)
                {
                    ValidateContactUpdate(context, target, allowPreImage: true, callerService, getWriterService, config, correlationId);
                }
            }
            else if (string.Equals(message, "UpdateMultiple", StringComparison.OrdinalIgnoreCase))
            {
                var targets = GetTargetEntities(context);
                if (targets != null)
                {
                    foreach (var target in targets.Entities)
                    {
                        ValidateContactUpdate(context, target, allowPreImage: false, callerService, getWriterService, config, correlationId);
                    }
                }
            }
            else if (IsSetStateMessage(message))
            {
                var moniker = GetEntityMoniker(context);
                var state = GetSetStateValue(context);
                if (moniker != null && state.HasValue)
                {
                    if (state.Value == CrmSchema.Contact.State.Inactive)
                    {
                        EnsureContactNotUsedInActiveOpenOpportunities(moniker.Id, getWriterService(), correlationId);
                    }
                    else if (state.Value == CrmSchema.Contact.State.Active)
                    {
                        var existing = RetrieveEntityOrThrow(
                            callerService,
                            CrmSchema.Entities.Contact,
                            moniker.Id,
                            new ColumnSet(CrmSchema.Contact.ParentCustomerId),
                            CrmError.CRM004,
                            "Contact parent Account could not be verified.",
                            correlationId);
                        var parentRef = existing.GetAttributeValue<EntityReference>(CrmSchema.Contact.ParentCustomerId);
                        EnsureParentAccountIsActive(parentRef, callerService, correlationId);
                    }
                }
            }
            else if (string.Equals(message, "Delete", StringComparison.OrdinalIgnoreCase))
            {
                var targetRef = GetTargetReference(context);
                if (targetRef != null)
                {
                    ValidateContactDelete(targetRef.Id, callerService, getWriterService, correlationId);
                }
            }
            else if (string.Equals(message, "DeleteMultiple", StringComparison.OrdinalIgnoreCase))
            {
                var targets = GetTargetReferences(context);
                if (targets != null)
                {
                    foreach (var targetRef in targets)
                    {
                        ValidateContactDelete(targetRef.Id, callerService, getWriterService, correlationId);
                    }
                }
            }
        }

        private static void ValidateContactCreate(
            Entity target,
            IOrganizationService callerService,
            CrmPolicyConfig config,
            Guid correlationId)
        {
            if (!target.Contains(CrmSchema.Contact.DoNotContact) || target[CrmSchema.Contact.DoNotContact] == null)
            {
                target[CrmSchema.Contact.DoNotContact] = false;
            }

            var email = target.GetAttributeValue<string>(CrmSchema.Contact.EmailAddress1);
            var phone = target.GetAttributeValue<string>(CrmSchema.Contact.MobilePhone);

            if (!target.Contains(CrmSchema.Contact.PreferredMethod) || target[CrmSchema.Contact.PreferredMethod] == null)
            {
                target[CrmSchema.Contact.PreferredMethod] = new OptionSetValue(
                    DomainValidators.ResolveDefaultPreferredMethod(email, phone));
            }

            var lastName = target.GetAttributeValue<string>(CrmSchema.Contact.LastName);
            var parentRef = target.GetAttributeValue<EntityReference>(CrmSchema.Contact.ParentCustomerId);
            var preferredMethod = GetOptionSetInt(target, CrmSchema.Contact.PreferredMethod);
            var currencyRef = target.GetAttributeValue<EntityReference>(CrmSchema.Contact.TransactionCurrencyId);

            DomainValidators.ValidateContact(
                lastName,
                parentRef != null ? parentRef.LogicalName : null,
                parentRef != null ? (Guid?)parentRef.Id : null,
                email,
                phone,
                preferredMethod,
                correlationId);

            DomainPricing.ValidateBaseCurrency(currencyRef != null ? (Guid?)currencyRef.Id : null, config, correlationId);
            EnsureParentAccountIsActive(parentRef, callerService, correlationId);
        }

        private static void ValidateContactUpdate(
            IPluginExecutionContext context,
            Entity target,
            bool allowPreImage,
            IOrganizationService callerService,
            Func<IOrganizationService> getWriterService,
            CrmPolicyConfig config,
            Guid correlationId)
        {
            var contactId = target.Id != Guid.Empty ? target.Id : context.PrimaryEntityId;
            var existing = LoadExistingEntity(context, CrmSchema.Entities.Contact, contactId, ContactColumns, allowPreImage, callerService, correlationId);
            var existingParent = existing.GetAttributeValue<EntityReference>(CrmSchema.Contact.ParentCustomerId);

            if (target.Contains(CrmSchema.Contact.ParentCustomerId))
            {
                var newParent = target.GetAttributeValue<EntityReference>(CrmSchema.Contact.ParentCustomerId);
                if (newParent == null
                    || !string.Equals(newParent.LogicalName, CrmSchema.Entities.Account, StringComparison.OrdinalIgnoreCase)
                    || existingParent == null
                    || existingParent.Id == Guid.Empty
                    || newParent.Id != existingParent.Id)
                {
                    CrmError.Throw(
                        CrmError.CRM004,
                        "Contact parent Account is immutable and cannot be changed or cleared after creation.",
                        correlationId);
                }
            }

            var merged = MergeOverExisting(CrmSchema.Entities.Contact, contactId, existing, target);

            var lastName = merged.GetAttributeValue<string>(CrmSchema.Contact.LastName);
            var mergedParent = merged.GetAttributeValue<EntityReference>(CrmSchema.Contact.ParentCustomerId);
            var email = merged.GetAttributeValue<string>(CrmSchema.Contact.EmailAddress1);
            var phone = merged.GetAttributeValue<string>(CrmSchema.Contact.MobilePhone);
            var preferredMethod = GetOptionSetInt(merged, CrmSchema.Contact.PreferredMethod);
            var currencyRef = merged.GetAttributeValue<EntityReference>(CrmSchema.Contact.TransactionCurrencyId);

            DomainValidators.ValidateContact(
                lastName,
                mergedParent != null ? mergedParent.LogicalName : null,
                mergedParent != null ? (Guid?)mergedParent.Id : null,
                email,
                phone,
                preferredMethod,
                correlationId);

            DomainPricing.ValidateBaseCurrency(currencyRef != null ? (Guid?)currencyRef.Id : null, config, correlationId);

            if (target.Contains(CrmSchema.Contact.StateCode))
            {
                var newState = GetOptionSetInt(target, CrmSchema.Contact.StateCode);
                if (newState.HasValue && newState.Value == CrmSchema.Contact.State.Active)
                {
                    EnsureParentAccountIsActive(mergedParent, callerService, correlationId);
                }
                else if (newState.HasValue && newState.Value == CrmSchema.Contact.State.Inactive)
                {
                    EnsureContactNotUsedInActiveOpenOpportunities(contactId, getWriterService(), correlationId);
                }
            }
        }

        private static void EnsureParentAccountIsActive(
            EntityReference parentRef,
            IOrganizationService callerService,
            Guid correlationId)
        {
            if (parentRef == null
                || parentRef.Id == Guid.Empty
                || !string.Equals(parentRef.LogicalName, CrmSchema.Entities.Account, StringComparison.OrdinalIgnoreCase))
            {
                CrmError.Throw(CrmError.CRM004, "Contact must belong to an active Account.", correlationId);
            }

            Entity parentAccount;
            try
            {
                parentAccount = callerService.Retrieve(
                    CrmSchema.Entities.Account,
                    parentRef.Id,
                    new ColumnSet(CrmSchema.Account.StateCode));
            }
            catch (FaultException<OrganizationServiceFault>)
            {
                CrmError.Throw(CrmError.CRM004, "Contact must belong to an active Account.", correlationId);
                return;
            }

            if (parentAccount == null
                || GetOptionSetInt(parentAccount, CrmSchema.Account.StateCode) != CrmSchema.Account.State.Active)
            {
                CrmError.Throw(CrmError.CRM004, "Contact must belong to an active Account.", correlationId);
            }
        }

        private static void EnsureContactNotUsedInActiveOpenOpportunities(
            Guid contactId,
            IOrganizationService writerService,
            Guid correlationId)
        {
            if (HasAnyRecord(
                writerService,
                CrmSchema.Entities.Opportunity,
                new ConditionExpression(CrmSchema.Opportunity.PrimaryContact, ConditionOperator.Equal, contactId),
                new ConditionExpression(CrmSchema.Opportunity.StateCode, ConditionOperator.Equal, CrmSchema.Opportunity.State.Active)))
            {
                CrmError.Throw(
                    CrmError.CRM017,
                    "Cannot deactivate a Contact referenced by an active Open Opportunity.",
                    correlationId);
            }
        }

        private static void ValidateContactDelete(
            Guid contactId,
            IOrganizationService callerService,
            Func<IOrganizationService> getWriterService,
            Guid correlationId)
        {
            if (HasAnyRecord(
                getWriterService(),
                CrmSchema.Entities.Opportunity,
                new ConditionExpression(CrmSchema.Opportunity.PrimaryContact, ConditionOperator.Equal, contactId)))
            {
                CrmError.Throw(
                    CrmError.CRM017,
                    "Contact cannot be deleted because it is referenced by existing Opportunities.",
                    correlationId);
            }

            if (HasAnyRecord(
                callerService,
                CrmSchema.Entities.Lead,
                new ConditionExpression(CrmSchema.Lead.Contact, ConditionOperator.Equal, contactId)))
            {
                CrmError.Throw(
                    CrmError.CRM017,
                    "Contact cannot be deleted because it is referenced by historical Leads.",
                    correlationId);
            }

            if (HasAnyRecord(
                callerService,
                CrmSchema.Entities.Account,
                new ConditionExpression(CrmSchema.Account.PrimaryContactId, ConditionOperator.Equal, contactId)))
            {
                CrmError.Throw(
                    CrmError.CRM017,
                    "Contact cannot be deleted because it is set as an Account Primary Contact.",
                    correlationId);
            }
        }

        private static void HandleProduct(
            IPluginExecutionContext context,
            string message,
            IOrganizationService callerService,
            CrmPolicyConfig config,
            Guid correlationId)
        {
            if (string.Equals(message, "Create", StringComparison.OrdinalIgnoreCase))
            {
                var target = GetTargetEntity(context);
                if (target != null)
                {
                    ValidateProductCreate(target, config, correlationId);
                }
            }
            else if (string.Equals(message, "CreateMultiple", StringComparison.OrdinalIgnoreCase))
            {
                var targets = GetTargetEntities(context);
                if (targets != null)
                {
                    foreach (var target in targets.Entities)
                    {
                        ValidateProductCreate(target, config, correlationId);
                    }
                }
            }
            else if (string.Equals(message, "Update", StringComparison.OrdinalIgnoreCase))
            {
                var target = GetTargetEntity(context);
                if (target != null)
                {
                    ValidateProductUpdate(context, target, allowPreImage: true, callerService, config, correlationId);
                }
            }
            else if (string.Equals(message, "UpdateMultiple", StringComparison.OrdinalIgnoreCase))
            {
                var targets = GetTargetEntities(context);
                if (targets != null)
                {
                    foreach (var target in targets.Entities)
                    {
                        ValidateProductUpdate(context, target, allowPreImage: false, callerService, config, correlationId);
                    }
                }
            }
            else if (string.Equals(message, "Delete", StringComparison.OrdinalIgnoreCase))
            {
                var targetRef = GetTargetReference(context);
                if (targetRef != null)
                {
                    ValidateProductDelete(targetRef.Id, callerService, correlationId);
                }
            }
            else if (string.Equals(message, "DeleteMultiple", StringComparison.OrdinalIgnoreCase))
            {
                var targets = GetTargetReferences(context);
                if (targets != null)
                {
                    foreach (var targetRef in targets)
                    {
                        ValidateProductDelete(targetRef.Id, callerService, correlationId);
                    }
                }
            }
        }

        private static void ValidateProductCreate(
            Entity target,
            CrmPolicyConfig config,
            Guid correlationId)
        {
            if (!target.Contains(CrmSchema.Product.TransactionCurrencyId)
                || target[CrmSchema.Product.TransactionCurrencyId] == null)
            {
                target[CrmSchema.Product.TransactionCurrencyId] = new EntityReference(
                    CrmSchema.Entities.TransactionCurrency,
                    config.BaseCurrencyId);
            }

            var name = target.GetAttributeValue<string>(CrmSchema.Product.Name);
            var sku = target.GetAttributeValue<string>(CrmSchema.Product.Sku);
            var description = target.GetAttributeValue<string>(CrmSchema.Product.Description);
            var unitPriceMoney = target.GetAttributeValue<Money>(CrmSchema.Product.UnitPrice);
            var currencyRef = target.GetAttributeValue<EntityReference>(CrmSchema.Product.TransactionCurrencyId);

            DomainValidators.ValidateProduct(
                name,
                sku,
                description,
                unitPriceMoney != null ? (decimal?)unitPriceMoney.Value : null,
                currencyRef != null ? (Guid?)currencyRef.Id : null,
                config,
                correlationId);
        }

        private static void ValidateProductUpdate(
            IPluginExecutionContext context,
            Entity target,
            bool allowPreImage,
            IOrganizationService callerService,
            CrmPolicyConfig config,
            Guid correlationId)
        {
            var productId = target.Id != Guid.Empty ? target.Id : context.PrimaryEntityId;
            var existing = LoadExistingEntity(context, CrmSchema.Entities.Product, productId, ProductColumns, allowPreImage, callerService, correlationId);
            var merged = MergeOverExisting(CrmSchema.Entities.Product, productId, existing, target);

            var name = merged.GetAttributeValue<string>(CrmSchema.Product.Name);
            var sku = merged.GetAttributeValue<string>(CrmSchema.Product.Sku);
            var description = merged.GetAttributeValue<string>(CrmSchema.Product.Description);
            var unitPriceMoney = merged.GetAttributeValue<Money>(CrmSchema.Product.UnitPrice);
            var currencyRef = merged.GetAttributeValue<EntityReference>(CrmSchema.Product.TransactionCurrencyId);

            DomainValidators.ValidateProduct(
                name,
                sku,
                description,
                unitPriceMoney != null ? (decimal?)unitPriceMoney.Value : null,
                currencyRef != null ? (Guid?)currencyRef.Id : null,
                config,
                correlationId);

            if (target.Contains(CrmSchema.Product.Sku))
            {
                var existingSku = existing.GetAttributeValue<string>(CrmSchema.Product.Sku);
                var newSku = target.GetAttributeValue<string>(CrmSchema.Product.Sku);
                if (!string.Equals(newSku, existingSku, StringComparison.Ordinal))
                {
                    if (HasAnyRecord(
                        callerService,
                        CrmSchema.Entities.OpportunityLine,
                        new ConditionExpression(CrmSchema.OpportunityLine.Product, ConditionOperator.Equal, productId)))
                    {
                        CrmError.Throw(
                            CrmError.CRM010,
                            "Product SKU cannot be modified because the product is referenced by existing Opportunity Lines.",
                            correlationId);
                    }
                }
            }
        }

        private static void ValidateProductDelete(
            Guid productId,
            IOrganizationService callerService,
            Guid correlationId)
        {
            if (HasAnyRecord(
                callerService,
                CrmSchema.Entities.OpportunityLine,
                new ConditionExpression(CrmSchema.OpportunityLine.Product, ConditionOperator.Equal, productId)))
            {
                CrmError.Throw(
                    CrmError.CRM017,
                    "Product cannot be deleted because it is referenced by existing Opportunity Lines.",
                    correlationId);
            }
        }

        private static Entity LoadExistingEntity(
            IPluginExecutionContext context,
            string entityName,
            Guid id,
            string[] columns,
            bool allowPreImage,
            IOrganizationService callerService,
            Guid correlationId)
        {
            if (allowPreImage
                && context.PreEntityImages != null
                && context.PreEntityImages.Contains(PreImageName)
                && context.PreEntityImages[PreImageName] != null)
            {
                return context.PreEntityImages[PreImageName];
            }

            return RetrieveEntityOrThrow(
                callerService,
                entityName,
                id,
                new ColumnSet(columns),
                CrmError.CRM008,
                "Existing record could not be loaded for validation.",
                correlationId);
        }

        private static Entity RetrieveEntityOrThrow(
            IOrganizationService service,
            string entityName,
            Guid id,
            ColumnSet columnSet,
            string errorCode,
            string message,
            Guid correlationId)
        {
            if (id == Guid.Empty)
            {
                CrmError.Throw(errorCode, message, correlationId);
            }

            try
            {
                var entity = service.Retrieve(entityName, id, columnSet);
                if (entity == null)
                {
                    CrmError.Throw(errorCode, message, correlationId);
                }

                return entity;
            }
            catch (FaultException<OrganizationServiceFault>)
            {
                CrmError.Throw(errorCode, message, correlationId);
                return null;
            }
        }

        private static Entity MergeOverExisting(string entityName, Guid id, Entity existing, Entity target)
        {
            var merged = new Entity(entityName, id);
            if (existing != null)
            {
                foreach (var kvp in existing.Attributes)
                {
                    merged[kvp.Key] = kvp.Value;
                }
            }

            if (target != null)
            {
                foreach (var kvp in target.Attributes)
                {
                    merged[kvp.Key] = kvp.Value;
                }
            }

            return merged;
        }

        private static bool HasAnyRecord(
            IOrganizationService service,
            string entityLogicalName,
            params ConditionExpression[] conditions)
        {
            var query = new QueryExpression(entityLogicalName)
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1,
                NoLock = true
            };

            if (conditions != null)
            {
                for (int i = 0; i < conditions.Length; i++)
                {
                    query.Criteria.AddCondition(conditions[i]);
                }
            }

            var result = service.RetrieveMultiple(query);
            return result != null && result.Entities != null && result.Entities.Count > 0;
        }

        private static Entity GetTargetEntity(IPluginExecutionContext context)
        {
            if (context.InputParameters != null && context.InputParameters.Contains("Target"))
            {
                return context.InputParameters["Target"] as Entity;
            }

            return null;
        }

        private static EntityCollection GetTargetEntities(IPluginExecutionContext context)
        {
            if (context.InputParameters != null && context.InputParameters.Contains("Targets"))
            {
                return context.InputParameters["Targets"] as EntityCollection;
            }

            return null;
        }

        private static EntityReference GetTargetReference(IPluginExecutionContext context)
        {
            if (context.InputParameters != null && context.InputParameters.Contains("Target"))
            {
                var targetRef = context.InputParameters["Target"] as EntityReference;
                if (targetRef != null)
                {
                    return targetRef;
                }
            }

            if (context.PrimaryEntityId != Guid.Empty && !string.IsNullOrWhiteSpace(context.PrimaryEntityName))
            {
                return new EntityReference(context.PrimaryEntityName, context.PrimaryEntityId);
            }

            return null;
        }

        private static EntityReferenceCollection GetTargetReferences(IPluginExecutionContext context)
        {
            if (context.InputParameters != null && context.InputParameters.Contains("Targets"))
            {
                return context.InputParameters["Targets"] as EntityReferenceCollection;
            }

            return null;
        }

        private static bool IsSetStateMessage(string message)
        {
            return string.Equals(message, "SetState", StringComparison.OrdinalIgnoreCase)
                || string.Equals(message, "SetStateDynamicEntity", StringComparison.OrdinalIgnoreCase);
        }

        private static EntityReference GetEntityMoniker(IPluginExecutionContext context)
        {
            if (context.InputParameters != null && context.InputParameters.Contains("EntityMoniker"))
            {
                return context.InputParameters["EntityMoniker"] as EntityReference;
            }

            return null;
        }

        private static int? GetSetStateValue(IPluginExecutionContext context)
        {
            if (context.InputParameters != null && context.InputParameters.Contains("State"))
            {
                var raw = context.InputParameters["State"];
                var opt = raw as OptionSetValue;
                if (opt != null)
                {
                    return opt.Value;
                }

                if (raw is int)
                {
                    return (int)raw;
                }

                var str = raw as string;
                if (!string.IsNullOrWhiteSpace(str))
                {
                    var trimmed = str.Trim();
                    if (string.Equals(trimmed, "Active", StringComparison.OrdinalIgnoreCase) || trimmed == "0")
                    {
                        return 0;
                    }

                    if (string.Equals(trimmed, "Inactive", StringComparison.OrdinalIgnoreCase) || trimmed == "1")
                    {
                        return 1;
                    }
                }
            }

            return null;
        }

        private static int? GetOptionSetInt(Entity entity, string attributeName)
        {
            var opt = entity.GetAttributeValue<OptionSetValue>(attributeName);
            return opt != null ? (int?)opt.Value : null;
        }
    }
}
