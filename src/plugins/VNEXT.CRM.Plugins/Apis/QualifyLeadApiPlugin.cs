using System;
using System.ServiceModel;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using VNEXT.CRM.Plugins.Core;
using VNEXT.CRM.Plugins.Domain;
using VNEXT.CRM.Plugins.Services;

namespace VNEXT.CRM.Plugins.Apis
{
    public sealed class QualifyLeadApiPlugin : IPlugin
    {
        private const int DuplicateKeyErrorCode1 = -2147088238; // 0x80060892
        private const int DuplicateKeyErrorCode2 = -2147220937; // 0x80040237

        private static readonly string[] LeadColumns =
        {
            CrmSchema.Lead.StateCode,
            CrmSchema.Lead.StatusCode,
            CrmSchema.Lead.OwnerId,
            CrmSchema.Lead.Name,
            CrmSchema.Lead.FirstName,
            CrmSchema.Lead.LastName,
            CrmSchema.Lead.CompanyName,
            CrmSchema.Lead.Email,
            CrmSchema.Lead.Phone,
            CrmSchema.Lead.Need,
            CrmSchema.Lead.NextStep,
            CrmSchema.Lead.NextFollowUpDate,
            CrmSchema.Lead.Budget,
            CrmSchema.Lead.BuyingTargetDate,
            CrmSchema.Lead.TransactionCurrencyId,
            CrmSchema.Lead.Account,
            CrmSchema.Lead.Contact,
            CrmSchema.Lead.Opportunity,
            CrmSchema.Lead.QualifiedOn
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

            ConcurrencyService.EnsureSynchronousTransaction(context);

            var correlationId = context.CorrelationId;
            if (tracing != null)
            {
                tracing.Trace("EXT10 QualifyLeadApiPlugin started. CorrelationId={0:D}", correlationId);
            }

            var targetRef = context.InputParameters != null && context.InputParameters.Contains(CrmSchema.Apis.Params.Target)
                ? context.InputParameters[CrmSchema.Apis.Params.Target] as EntityReference
                : null;

            if (targetRef == null
                || !string.Equals(targetRef.LogicalName, CrmSchema.Entities.Lead, StringComparison.OrdinalIgnoreCase)
                || targetRef.Id == Guid.Empty)
            {
                CrmError.Throw(CrmError.CRM002, "A valid Lead Target reference is required for vn_QualifyLead.", correlationId);
            }

            var leadId = targetRef.Id;
            var initiatingUserId = AuthorizationService.RequireInitiatingUserId(context);
            var callerService = AuthorizationService.CreateCallerService(factory, context);
            var config = CrmPolicyConfigLoader.Load(callerService, correlationId);
            var utcNow = UtcClock.EnsureUtc(DateTime.UtcNow);

            Entity lead;
            try
            {
                lead = callerService.Retrieve(CrmSchema.Entities.Lead, leadId, new ColumnSet(LeadColumns));
            }
            catch (FaultException<OrganizationServiceFault>)
            {
                CrmError.Throw(CrmError.CRM013, "Lead is inaccessible or does not exist.", correlationId);
                return;
            }

            if (lead == null)
            {
                CrmError.Throw(CrmError.CRM013, "Lead is inaccessible or does not exist.", correlationId);
            }

            var leadState = GetOptionSetInt(lead, CrmSchema.Lead.StateCode);
            var leadStatus = GetOptionSetInt(lead, CrmSchema.Lead.StatusCode);

            // Safe already-qualified readback BEFORE RowVersion enforcement (BR06 & §7.1)
            if (leadState == CrmSchema.Lead.State.Inactive && leadStatus == CrmSchema.Lead.Status.Qualified)
            {
                var storedAccount = lead.GetAttributeValue<EntityReference>(CrmSchema.Lead.Account);
                var storedContact = lead.GetAttributeValue<EntityReference>(CrmSchema.Lead.Contact);
                var storedOpportunity = lead.GetAttributeValue<EntityReference>(CrmSchema.Lead.Opportunity);

                if (storedAccount == null || storedAccount.Id == Guid.Empty
                    || storedContact == null || storedContact.Id == Guid.Empty
                    || storedOpportunity == null || storedOpportunity.Id == Guid.Empty)
                {
                    CrmError.Throw(CrmError.CRM005, "Already-qualified Lead is missing stored conversion links.", correlationId);
                }

                SetOutputs(context, storedAccount.Id, storedContact.Id, storedOpportunity.Id, wasAlreadyQualified: true, correlationId: correlationId);
                return;
            }

            if (leadState == CrmSchema.Lead.State.Inactive)
            {
                CrmError.Throw(CrmError.CRM005, "Disqualified Leads cannot be qualified.", correlationId);
            }

            // First-attempt authorization, RowVersion, readiness & internal writer pre-checks
            AuthorizationService.EnsureAccessRights(
                callerService,
                initiatingUserId,
                new EntityReference(CrmSchema.Entities.Lead, leadId),
                AccessRights.ReadAccess | AccessRights.WriteAccess,
                CrmError.CRM013,
                "Caller lacks Write permission to qualify this Lead.",
                correlationId);

            var rawRowVersion = GetOptionalStringInput(context, CrmSchema.Apis.Params.ExpectedRowVersion);
            var normalizedRowVersion = ConcurrencyService.NormalizeRowVersion(rawRowVersion, correlationId);
            if (!string.IsNullOrWhiteSpace(lead.RowVersion)
                && !string.Equals(lead.RowVersion, normalizedRowVersion, StringComparison.Ordinal))
            {
                CrmError.Throw(
                    CrmError.CRM021,
                    "Lead was modified by another operation. Refresh the record and retry.",
                    correlationId);
            }

            var opportunityName = GetOptionalStringInput(context, CrmSchema.Apis.Params.OpportunityName);
            var expectedCloseDate = GetOptionalDateTimeInput(context, CrmSchema.Apis.Params.ExpectedCloseDate);
            var existingAccountId = GetOptionalGuidInput(context, CrmSchema.Apis.Params.ExistingAccountId, correlationId);
            var existingContactId = GetOptionalGuidInput(context, CrmSchema.Apis.Params.ExistingContactId, correlationId);

            var companyName = lead.GetAttributeValue<string>(CrmSchema.Lead.CompanyName);
            var lastName = lead.GetAttributeValue<string>(CrmSchema.Lead.LastName);
            var email = lead.GetAttributeValue<string>(CrmSchema.Lead.Email);
            var phone = lead.GetAttributeValue<string>(CrmSchema.Lead.Phone);
            var need = lead.GetAttributeValue<string>(CrmSchema.Lead.Need);
            var nextStep = lead.GetAttributeValue<string>(CrmSchema.Lead.NextStep);
            var nextFollowUpDate = lead.GetAttributeValue<DateTime?>(CrmSchema.Lead.NextFollowUpDate);
            var budgetMoney = lead.GetAttributeValue<Money>(CrmSchema.Lead.Budget);
            var buyingTargetDate = lead.GetAttributeValue<DateTime?>(CrmSchema.Lead.BuyingTargetDate);
            var currencyRef = lead.GetAttributeValue<EntityReference>(CrmSchema.Lead.TransactionCurrencyId);

            DomainValidators.ValidateLeadQualificationReadiness(
                leadState ?? -1,
                leadStatus ?? -1,
                companyName,
                lastName,
                email,
                phone,
                need,
                nextStep,
                nextFollowUpDate,
                budgetMoney != null ? (decimal?)budgetMoney.Value : null,
                currencyRef != null ? (Guid?)currencyRef.Id : null,
                opportunityName,
                expectedCloseDate,
                utcNow,
                config,
                correlationId);

            var selectionPath = DomainValidators.ResolveCustomerSelectionPath(existingAccountId, existingContactId, correlationId);

            var leadOwnerRef = lead.GetAttributeValue<EntityReference>(CrmSchema.Lead.OwnerId);
            if (leadOwnerRef == null
                || !string.Equals(leadOwnerRef.LogicalName, CrmSchema.Entities.SystemUser, StringComparison.OrdinalIgnoreCase)
                || leadOwnerRef.Id == Guid.Empty)
            {
                CrmError.Throw(CrmError.CRM002, "Lead must have a valid enabled System User owner to qualify.", correlationId);
            }

            // Validate Scoped Internal Writer and Lead owner BEFORE creating any records
            var writerService = AuthorizationService.CreateInternalWriterService(factory, config, correlationId);
            AuthorizationService.RetrieveEnabledUser(writerService, leadOwnerRef.Id, correlationId);

            // Execute exact 3-path customer selection (BR04, BR05, §7.1)
            Guid resolvedAccountId;
            Guid resolvedContactId;

            switch (selectionPath)
            {
                case CustomerSelectionPath.ReuseAccountAndContact:
                    ValidateReusableActiveAccount(callerService, initiatingUserId, existingAccountId.Value, correlationId);
                    ValidateReusableActiveMatchingContact(callerService, initiatingUserId, existingAccountId.Value, existingContactId.Value, correlationId);
                    resolvedAccountId = existingAccountId.Value;
                    resolvedContactId = existingContactId.Value;
                    break;

                case CustomerSelectionPath.ReuseAccountCreateContact:
                    ValidateReusableActiveAccount(callerService, initiatingUserId, existingAccountId.Value, correlationId);
                    resolvedAccountId = existingAccountId.Value;
                    resolvedContactId = callerService.Create(BuildNewContactEntity(lead, resolvedAccountId, config.BaseCurrencyId));
                    TransferOwnerIfNeeded(callerService, CrmSchema.Entities.Contact, resolvedContactId, initiatingUserId, leadOwnerRef);
                    break;

                case CustomerSelectionPath.CreateAccountAndContact:
                    resolvedAccountId = callerService.Create(BuildNewAccountEntity(companyName, config.BaseCurrencyId));
                    resolvedContactId = callerService.Create(BuildNewContactEntity(lead, resolvedAccountId, config.BaseCurrencyId));

                    var primaryContactPatch = new Entity(CrmSchema.Entities.Account, resolvedAccountId);
                    primaryContactPatch[CrmSchema.Account.PrimaryContactId] = new EntityReference(CrmSchema.Entities.Contact, resolvedContactId);
                    callerService.Update(primaryContactPatch);

                    TransferOwnerIfNeeded(callerService, CrmSchema.Entities.Contact, resolvedContactId, initiatingUserId, leadOwnerRef);
                    TransferOwnerIfNeeded(callerService, CrmSchema.Entities.Account, resolvedAccountId, initiatingUserId, leadOwnerRef);
                    break;

                default:
                    CrmError.Throw(CrmError.CRM003, "Unsupported customer selection path.", correlationId);
                    return;
            }

            // Create unique-origin Opportunity (BR03, BR05, BR06, BR10)
            var trimmedOppName = opportunityName.Trim();
            var newOpportunity = new Entity(CrmSchema.Entities.Opportunity);
            newOpportunity[CrmSchema.Opportunity.Name] = trimmedOppName;
            newOpportunity[CrmSchema.Opportunity.OriginatingLead] = new EntityReference(CrmSchema.Entities.Lead, leadId);
            newOpportunity[CrmSchema.Opportunity.Account] = new EntityReference(CrmSchema.Entities.Account, resolvedAccountId);
            newOpportunity[CrmSchema.Opportunity.PrimaryContact] = new EntityReference(CrmSchema.Entities.Contact, resolvedContactId);
            newOpportunity[CrmSchema.Opportunity.TransactionCurrencyId] = new EntityReference(CrmSchema.Entities.TransactionCurrency, config.BaseCurrencyId);
            newOpportunity[CrmSchema.Opportunity.Stage] = new OptionSetValue(CrmSchema.Opportunity.StageValues.Qualify);
            newOpportunity[CrmSchema.Opportunity.Probability] = CrmSchema.Opportunity.Probabilities.Qualify;
            newOpportunity[CrmSchema.Opportunity.ExpectedCloseDate] = UtcClock.NormalizeDateOnly(expectedCloseDate.Value);
            newOpportunity[CrmSchema.Opportunity.Need] = need;
            newOpportunity[CrmSchema.Opportunity.NextStep] = nextStep;
            newOpportunity[CrmSchema.Opportunity.NextFollowUpDate] = UtcClock.NormalizeDateOnly(nextFollowUpDate.Value);
            newOpportunity[CrmSchema.Opportunity.Total] = new Money(0m);
            newOpportunity[CrmSchema.Opportunity.MaxDiscount] = 0m;
            newOpportunity[CrmSchema.Opportunity.CommandSequence] = 0;
            newOpportunity[CrmSchema.Opportunity.CurrentApproval] = null;
            newOpportunity[CrmSchema.Opportunity.LastProgressOn] = utcNow;
            newOpportunity[CrmSchema.Opportunity.StageEnteredOn] = utcNow;

            if (budgetMoney != null)
            {
                newOpportunity[CrmSchema.Opportunity.Budget] = new Money(budgetMoney.Value);
            }

            if (buyingTargetDate.HasValue)
            {
                newOpportunity[CrmSchema.Opportunity.BuyingTargetDate] = UtcClock.NormalizeDateOnly(buyingTargetDate.Value);
            }

            Guid createdOpportunityId;
            try
            {
                createdOpportunityId = callerService.Create(newOpportunity);
            }
            catch (FaultException<OrganizationServiceFault> ex)
            {
                if (IsDuplicateKeyFault(ex))
                {
                    CrmError.Throw(
                        CrmError.CRM021,
                        "Lead was already qualified by a concurrent operation. Refresh the Lead to read its conversion links.",
                        correlationId);
                }

                throw;
            }

            TransferOwnerIfNeeded(callerService, CrmSchema.Entities.Opportunity, createdOpportunityId, initiatingUserId, leadOwnerRef);

            // Conditionally close Lead with exact PL02 EXT10 allowlist fields
            var leadClosePatch = new Entity(CrmSchema.Entities.Lead, leadId);
            leadClosePatch[CrmSchema.Lead.StateCode] = new OptionSetValue(CrmSchema.Lead.State.Inactive);
            leadClosePatch[CrmSchema.Lead.StatusCode] = new OptionSetValue(CrmSchema.Lead.Status.Qualified);
            leadClosePatch[CrmSchema.Lead.Account] = new EntityReference(CrmSchema.Entities.Account, resolvedAccountId);
            leadClosePatch[CrmSchema.Lead.Contact] = new EntityReference(CrmSchema.Entities.Contact, resolvedContactId);
            leadClosePatch[CrmSchema.Lead.Opportunity] = new EntityReference(CrmSchema.Entities.Opportunity, createdOpportunityId);
            leadClosePatch[CrmSchema.Lead.QualifiedOn] = utcNow;

            ConcurrencyService.UpdateIfRowVersionMatches(callerService, leadClosePatch, normalizedRowVersion, correlationId);

            // Atomically mirror single Qualify BPF instance via Scoped Internal Writer (F-02)
            BpfMirrorService.EnsureSingleQualifyInstance(writerService, createdOpportunityId, trimmedOppName, correlationId);

            SetOutputs(context, resolvedAccountId, resolvedContactId, createdOpportunityId, wasAlreadyQualified: false, correlationId: correlationId);
        }

        private static void ValidateReusableActiveAccount(
            IOrganizationService callerService,
            Guid initiatingUserId,
            Guid accountId,
            Guid correlationId)
        {
            Entity account;
            try
            {
                account = callerService.Retrieve(
                    CrmSchema.Entities.Account,
                    accountId,
                    new ColumnSet(CrmSchema.Account.StateCode));
            }
            catch (FaultException<OrganizationServiceFault>)
            {
                CrmError.Throw(CrmError.CRM003, "Selected Account is inaccessible or does not exist.", correlationId);
                return;
            }

            if (account == null || GetOptionSetInt(account, CrmSchema.Account.StateCode) != CrmSchema.Account.State.Active)
            {
                CrmError.Throw(CrmError.CRM003, "Selected Account must be Active.", correlationId);
            }

            AuthorizationService.EnsureAccessRights(
                callerService,
                initiatingUserId,
                new EntityReference(CrmSchema.Entities.Account, accountId),
                AccessRights.ReadAccess | AccessRights.AppendToAccess,
                CrmError.CRM003,
                "Caller lacks required Read and AppendTo permissions on the selected Account.",
                correlationId);
        }

        private static void ValidateReusableActiveMatchingContact(
            IOrganizationService callerService,
            Guid initiatingUserId,
            Guid accountId,
            Guid contactId,
            Guid correlationId)
        {
            Entity contact;
            try
            {
                contact = callerService.Retrieve(
                    CrmSchema.Entities.Contact,
                    contactId,
                    new ColumnSet(CrmSchema.Contact.StateCode, CrmSchema.Contact.ParentCustomerId));
            }
            catch (FaultException<OrganizationServiceFault>)
            {
                CrmError.Throw(CrmError.CRM004, "Selected Contact is inaccessible or does not exist.", correlationId);
                return;
            }

            if (contact == null || GetOptionSetInt(contact, CrmSchema.Contact.StateCode) != CrmSchema.Contact.State.Active)
            {
                CrmError.Throw(CrmError.CRM004, "Selected Contact must be Active.", correlationId);
            }

            var parentRef = contact.GetAttributeValue<EntityReference>(CrmSchema.Contact.ParentCustomerId);
            if (parentRef == null
                || !string.Equals(parentRef.LogicalName, CrmSchema.Entities.Account, StringComparison.OrdinalIgnoreCase)
                || parentRef.Id != accountId)
            {
                CrmError.Throw(CrmError.CRM004, "Selected Contact does not belong to the selected Account.", correlationId);
            }

            AuthorizationService.EnsureAccessRights(
                callerService,
                initiatingUserId,
                new EntityReference(CrmSchema.Entities.Contact, contactId),
                AccessRights.ReadAccess | AccessRights.AppendToAccess,
                CrmError.CRM004,
                "Caller lacks required Read and AppendTo permissions on the selected Contact.",
                correlationId);
        }

        private static Entity BuildNewAccountEntity(string companyName, Guid baseCurrencyId)
        {
            var account = new Entity(CrmSchema.Entities.Account);
            account[CrmSchema.Account.Name] = companyName.Trim();
            account[CrmSchema.Account.AccountType] = new OptionSetValue(CrmSchema.Account.AccountTypeValues.Prospect);
            account[CrmSchema.Account.TransactionCurrencyId] = new EntityReference(CrmSchema.Entities.TransactionCurrency, baseCurrencyId);
            return account;
        }

        private static Entity BuildNewContactEntity(Entity lead, Guid accountId, Guid baseCurrencyId)
        {
            var firstName = lead.GetAttributeValue<string>(CrmSchema.Lead.FirstName);
            var lastName = lead.GetAttributeValue<string>(CrmSchema.Lead.LastName);
            var email = lead.GetAttributeValue<string>(CrmSchema.Lead.Email);
            var phone = lead.GetAttributeValue<string>(CrmSchema.Lead.Phone);

            var contact = new Entity(CrmSchema.Entities.Contact);
            contact[CrmSchema.Contact.ParentCustomerId] = new EntityReference(CrmSchema.Entities.Account, accountId);
            contact[CrmSchema.Contact.LastName] = lastName.Trim();
            contact[CrmSchema.Contact.PreferredMethod] = new OptionSetValue(
                DomainValidators.ResolveDefaultPreferredMethod(email, phone));
            contact[CrmSchema.Contact.DoNotContact] = false;
            contact[CrmSchema.Contact.TransactionCurrencyId] = new EntityReference(CrmSchema.Entities.TransactionCurrency, baseCurrencyId);

            if (!string.IsNullOrWhiteSpace(firstName))
            {
                contact[CrmSchema.Contact.FirstName] = firstName.Trim();
            }

            if (!string.IsNullOrWhiteSpace(email))
            {
                contact[CrmSchema.Contact.EmailAddress1] = email.Trim();
            }

            if (!string.IsNullOrWhiteSpace(phone))
            {
                contact[CrmSchema.Contact.MobilePhone] = phone.Trim();
            }

            return contact;
        }

        private static void TransferOwnerIfNeeded(
            IOrganizationService callerService,
            string entityLogicalName,
            Guid recordId,
            Guid initiatingUserId,
            EntityReference leadOwnerRef)
        {
            if (leadOwnerRef.Id == initiatingUserId)
            {
                return;
            }

            var patch = new Entity(entityLogicalName, recordId);
            patch[CrmSchema.Common.OwnerId] = leadOwnerRef;
            callerService.Update(patch);
        }

        private static void SetOutputs(
            IPluginExecutionContext context,
            Guid accountId,
            Guid contactId,
            Guid opportunityId,
            bool wasAlreadyQualified,
            Guid correlationId)
        {
            context.OutputParameters[CrmSchema.Apis.Params.AccountId] = accountId;
            context.OutputParameters[CrmSchema.Apis.Params.ContactId] = contactId;
            context.OutputParameters[CrmSchema.Apis.Params.OpportunityId] = opportunityId;
            context.OutputParameters[CrmSchema.Apis.Params.WasAlreadyQualified] = wasAlreadyQualified;
            context.OutputParameters[CrmSchema.Apis.Params.CorrelationId] = correlationId.ToString("D");
        }

        private static bool IsDuplicateKeyFault(FaultException<OrganizationServiceFault> ex)
        {
            if (ex == null)
            {
                return false;
            }

            if (ex.Detail != null)
            {
                if (ex.Detail.ErrorCode == DuplicateKeyErrorCode1 || ex.Detail.ErrorCode == DuplicateKeyErrorCode2)
                {
                    return true;
                }

                if (MessageIndicatesDuplicateKey(ex.Detail.Message))
                {
                    return true;
                }
            }

            return MessageIndicatesDuplicateKey(ex.Message);
        }

        private static bool MessageIndicatesDuplicateKey(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            return message.IndexOf("duplicate key", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("vn_originatinglead", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("cannot insert duplicate", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetOptionalStringInput(IPluginExecutionContext context, string paramName)
        {
            if (context.InputParameters != null && context.InputParameters.Contains(paramName))
            {
                return context.InputParameters[paramName] as string;
            }

            return null;
        }

        private static DateTime? GetOptionalDateTimeInput(IPluginExecutionContext context, string paramName)
        {
            if (context.InputParameters != null && context.InputParameters.Contains(paramName))
            {
                var raw = context.InputParameters[paramName];
                if (raw is DateTime)
                {
                    var dt = (DateTime)raw;
                    return dt == DateTime.MinValue ? (DateTime?)null : dt;
                }
            }

            return null;
        }

        private static Guid? GetOptionalGuidInput(IPluginExecutionContext context, string paramName, Guid correlationId)
        {
            if (context.InputParameters == null || !context.InputParameters.Contains(paramName))
            {
                return null;
            }

            var raw = context.InputParameters[paramName];
            if (raw == null)
            {
                return null;
            }

            if (raw is Guid)
            {
                var g = (Guid)raw;
                return g == Guid.Empty ? (Guid?)null : g;
            }

            var text = raw as string;
            if (text != null)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }

                Guid parsed;
                if (Guid.TryParse(text.Trim(), out parsed))
                {
                    return parsed;
                }

                CrmError.Throw(CrmError.CRM003, string.Format("Parameter '{0}' is not a valid GUID.", paramName), correlationId);
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
