using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using VNEXT.CRM.Plugins.Core;
using VNEXT.CRM.Plugins.Guards;
using Xunit;

namespace VNEXT.CRM.Plugins.Tests
{
    public class SalesGuardTests
    {
        private static readonly Guid CorrelationId = new Guid("a1a1a1a1-a1a1-a1a1-a1a1-a1a1a1a1a1a1");
        private static readonly Guid CallerUserId = new Guid("b2b2b2b2-b2b2-b2b2-b2b2-b2b2b2b2b2b2");
        private static readonly Guid OtherUserId = new Guid("b3b3b3b3-b3b3-b3b3-b3b3-b3b3b3b3b3b3");
        private static readonly Guid WriterUserId = new Guid("c3c3c3c3-c3c3-c3c3-c3c3-c3c3c3c3c3c3");
        private static readonly Guid BaseCurrencyId = new Guid("d4d4d4d4-d4d4-d4d4-d4d4-d4d4d4d4d4d4");
        private static readonly Guid LeadId = new Guid("e5e5e5e5-e5e5-e5e5-e5e5-e5e5e5e5e5e5");
        private static readonly Guid OtherLeadId = new Guid("e6e6e6e6-e6e6-e6e6-e6e6-e6e6e6e6e6e6");
        private static readonly Guid OppId = new Guid("f7f7f7f7-f7f7-f7f7-f7f7-f7f7f7f7f7f7");
        private static readonly Guid OtherOppId = new Guid("f8f8f8f8-f8f8-f8f8-f8f8-f8f8f8f8f8f8");
        private static readonly Guid LineId = new Guid("19191919-1919-1919-1919-191919191919");
        private static readonly Guid ApprovalId = new Guid("29292929-2929-2929-2929-292929292929");
        private static readonly Guid BpfId = new Guid("39393939-3939-3939-3939-393939393939");
        private static readonly Guid AccountId = new Guid("49494949-4949-4949-4949-494949494949");
        private static readonly Guid ContactId = new Guid("59595959-5959-5959-5959-595959595959");

        private static void AssertCrmError(string expectedCode, Action action)
        {
            var ex = Assert.Throws<InvalidPluginExecutionException>(action);
            Assert.StartsWith("[" + expectedCode + "] ", ex.Message);
            Assert.Contains("(CorrelationId: " + CorrelationId.ToString("D") + ")", ex.Message);
        }

        private static FakeOrganizationService CreateBaseService(
            Func<string, Guid, ColumnSet, Entity> onRetrieve = null)
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
                            var def = new Entity(CrmSchema.Entities.EnvironmentVariableDefinition, Guid.NewGuid());
                            def[CrmSchema.Platform.EnvVarSchemaName] = CrmSchema.EnvVars.InternalWriterUserId;
                            def["val." + CrmSchema.Platform.EnvVarValue] = new AliasedValue(
                                CrmSchema.Entities.EnvironmentVariableValue,
                                CrmSchema.Platform.EnvVarValue,
                                WriterUserId.ToString("D"));
                            return new EntityCollection(new[] { def });
                        }

                        if (qe.EntityName == CrmSchema.Entities.Organization)
                        {
                            var org = new Entity(CrmSchema.Entities.Organization, Guid.NewGuid());
                            org[CrmSchema.Platform.OrganizationBaseCurrencyId] = new EntityReference(CrmSchema.Entities.TransactionCurrency, BaseCurrencyId);
                            return new EntityCollection(new[] { org });
                        }
                    }

                    return new EntityCollection();
                }
            };
        }

        private static FakePluginExecutionContext CreateApiParent(
            string apiMessage,
            string boundEntity,
            Guid boundId,
            string subOpParam = null,
            string subOpValue = null)
        {
            var inputs = new ParameterCollection
            {
                { CrmSchema.Apis.Params.Target, new EntityReference(boundEntity, boundId) }
            };
            if (subOpParam != null && subOpValue != null)
            {
                inputs[subOpParam] = subOpValue;
            }

            return new FakePluginExecutionContext
            {
                MessageName = apiMessage,
                Mode = 0,
                IsInTransaction = true,
                Stage = 30,
                Depth = 1,
                CorrelationId = CorrelationId,
                InitiatingUserId = CallerUserId,
                UserId = CallerUserId,
                InputParameters = inputs
            };
        }

        private static FakePluginExecutionContext CreateNestedContext(
            string entityName,
            string messageName,
            ParameterCollection inputs,
            IPluginExecutionContext parentContext = null,
            bool asWriter = false)
        {
            return new FakePluginExecutionContext
            {
                PrimaryEntityName = entityName,
                MessageName = messageName,
                Stage = 20,
                Mode = 0,
                IsInTransaction = true,
                Depth = parentContext != null ? 2 : 1,
                CorrelationId = CorrelationId,
                InitiatingUserId = CallerUserId,
                UserId = asWriter ? WriterUserId : CallerUserId,
                ParentContext = parentContext,
                InputParameters = inputs ?? new ParameterCollection()
            };
        }

        private static void RunPlugin(
            IPluginExecutionContext context,
            IOrganizationService callerService,
            IOrganizationService writerService = null)
        {
            var effectiveWriter = writerService ?? callerService;
            var factory = new FakeOrganizationServiceFactory(uid =>
                uid == CallerUserId ? callerService :
                uid == WriterUserId ? effectiveWriter : null);

            new SalesGuardPlugin().Execute(new FakeServiceProvider(context, factory));
        }

        [Fact]
        public void Lead_CreateContactDisqualifyImmutabilityAndTrustedExt10_EnforceAllRules()
        {
            var followUp = new DateTime(2026, 11, 25, 0, 0, 0, DateTimeKind.Utc);
            var caller = CreateBaseService(onRetrieve: (e, id, c) =>
                e == CrmSchema.Entities.Lead && id == OtherLeadId ? new Entity(CrmSchema.Entities.Lead, OtherLeadId) : null);

            // 1. Valid New Create defaults Source=Referral, Rating=Warm, Currency=Base
            var createTarget = new Entity(CrmSchema.Entities.Lead, LeadId);
            createTarget[CrmSchema.Lead.Name] = "Cloud CRM Rollout";
            createTarget[CrmSchema.Lead.LastName] = "Khoury";
            createTarget[CrmSchema.Lead.Email] = "khoury@contoso.example";
            createTarget[CrmSchema.Lead.NextFollowUpDate] = followUp;

            RunPlugin(CreateNestedContext(CrmSchema.Entities.Lead, "Create", new ParameterCollection { { "Target", createTarget } }), caller);
            Assert.Equal(CrmSchema.Lead.SourceValues.Referral, createTarget.GetAttributeValue<OptionSetValue>(CrmSchema.Lead.Source).Value);
            Assert.Equal(CrmSchema.Lead.RatingValues.Warm, createTarget.GetAttributeValue<OptionSetValue>(CrmSchema.Lead.Rating).Value);
            Assert.Equal(BaseCurrencyId, createTarget.GetAttributeValue<EntityReference>(CrmSchema.Lead.TransactionCurrencyId).Id);

            // 2. Contacted Update passes with nonblank NextStep
            var existingOpenLead = new Entity(CrmSchema.Entities.Lead, LeadId);
            existingOpenLead[CrmSchema.Lead.StateCode] = new OptionSetValue(CrmSchema.Lead.State.Active);
            existingOpenLead[CrmSchema.Lead.StatusCode] = new OptionSetValue(CrmSchema.Lead.Status.New);
            existingOpenLead[CrmSchema.Lead.OwnerId] = new EntityReference(CrmSchema.Entities.SystemUser, CallerUserId);
            existingOpenLead[CrmSchema.Lead.Name] = "Cloud CRM Rollout";
            existingOpenLead[CrmSchema.Lead.LastName] = "Khoury";
            existingOpenLead[CrmSchema.Lead.Source] = new OptionSetValue(CrmSchema.Lead.SourceValues.Referral);
            existingOpenLead[CrmSchema.Lead.Rating] = new OptionSetValue(CrmSchema.Lead.RatingValues.Warm);
            existingOpenLead[CrmSchema.Lead.Email] = "khoury@contoso.example";
            existingOpenLead[CrmSchema.Lead.NextFollowUpDate] = followUp;
            existingOpenLead[CrmSchema.Lead.TransactionCurrencyId] = new EntityReference(CrmSchema.Entities.TransactionCurrency, BaseCurrencyId);

            var contactedTarget = new Entity(CrmSchema.Entities.Lead, LeadId);
            contactedTarget[CrmSchema.Lead.StatusCode] = new OptionSetValue(CrmSchema.Lead.Status.Contacted);
            contactedTarget[CrmSchema.Lead.NextStep] = "Send architectural overview";
            var contactedCtx = CreateNestedContext(CrmSchema.Entities.Lead, "Update", new ParameterCollection { { "Target", contactedTarget } });
            contactedCtx.PreEntityImages["PreImage"] = existingOpenLead;
            RunPlugin(contactedCtx, caller);

            // 3. Disqualify Update stamps statecode=Inactive and vn_disqualifiedon
            var disqualifyTarget = new Entity(CrmSchema.Entities.Lead, LeadId);
            disqualifyTarget[CrmSchema.Lead.StatusCode] = new OptionSetValue(CrmSchema.Lead.Status.Disqualified);
            disqualifyTarget[CrmSchema.Lead.DisqualificationReason] = new OptionSetValue(CrmSchema.Lead.DisqualificationReasonValues.Duplicate);
            disqualifyTarget[CrmSchema.Lead.DisqualificationComments] = "Duplicate of prior inbound inquiry.";
            disqualifyTarget[CrmSchema.Lead.DuplicateOf] = new EntityReference(CrmSchema.Entities.Lead, OtherLeadId);
            var disqualifyCtx = CreateNestedContext(CrmSchema.Entities.Lead, "Update", new ParameterCollection { { "Target", disqualifyTarget } });
            disqualifyCtx.PreEntityImages["PreImage"] = existingOpenLead;
            RunPlugin(disqualifyCtx, caller);
            Assert.Equal(CrmSchema.Lead.State.Inactive, disqualifyTarget.GetAttributeValue<OptionSetValue>(CrmSchema.Lead.StateCode).Value);
            Assert.NotNull(disqualifyTarget.GetAttributeValue<DateTime?>(CrmSchema.Lead.DisqualifiedOn));

            // 4. Rejects { statecode = 1 } without statuscode = Disqualified -> CRM005
            var blindInactive = new Entity(CrmSchema.Entities.Lead, LeadId);
            blindInactive[CrmSchema.Lead.StateCode] = new OptionSetValue(CrmSchema.Lead.State.Inactive);
            var blindInactiveCtx = CreateNestedContext(CrmSchema.Entities.Lead, "Update", new ParameterCollection { { "Target", blindInactive } });
            blindInactiveCtx.PreEntityImages["PreImage"] = existingOpenLead;
            AssertCrmError(CrmError.CRM005, () => RunPlugin(blindInactiveCtx, caller));

            // 5. Rejects self-Duplicate & non-Duplicate with duplicateOf & missing comments -> CRM001
            var selfDup = new Entity(CrmSchema.Entities.Lead, LeadId);
            selfDup[CrmSchema.Lead.StatusCode] = new OptionSetValue(CrmSchema.Lead.Status.Disqualified);
            selfDup[CrmSchema.Lead.DisqualificationReason] = new OptionSetValue(CrmSchema.Lead.DisqualificationReasonValues.Duplicate);
            selfDup[CrmSchema.Lead.DisqualificationComments] = "Self ref";
            selfDup[CrmSchema.Lead.DuplicateOf] = new EntityReference(CrmSchema.Entities.Lead, LeadId);
            var selfDupCtx = CreateNestedContext(CrmSchema.Entities.Lead, "Update", new ParameterCollection { { "Target", selfDup } });
            selfDupCtx.PreEntityImages["PreImage"] = existingOpenLead;
            AssertCrmError(CrmError.CRM001, () => RunPlugin(selfDupCtx, caller));

            var nonDupWithRef = new Entity(CrmSchema.Entities.Lead, LeadId);
            nonDupWithRef[CrmSchema.Lead.StatusCode] = new OptionSetValue(CrmSchema.Lead.Status.Disqualified);
            nonDupWithRef[CrmSchema.Lead.DisqualificationReason] = new OptionSetValue(CrmSchema.Lead.DisqualificationReasonValues.NoBudget);
            nonDupWithRef[CrmSchema.Lead.DisqualificationComments] = "No budget";
            nonDupWithRef[CrmSchema.Lead.DuplicateOf] = new EntityReference(CrmSchema.Entities.Lead, OtherLeadId);
            var nonDupCtx = CreateNestedContext(CrmSchema.Entities.Lead, "Update", new ParameterCollection { { "Target", nonDupWithRef } });
            nonDupCtx.PreEntityImages["PreImage"] = existingOpenLead;
            AssertCrmError(CrmError.CRM001, () => RunPlugin(nonDupCtx, caller));

            // 6. Closed Lead edit/reopen -> CRM005
            var existingClosedLead = new Entity(CrmSchema.Entities.Lead, LeadId);
            existingClosedLead[CrmSchema.Lead.StateCode] = new OptionSetValue(CrmSchema.Lead.State.Inactive);
            var reopenTarget = new Entity(CrmSchema.Entities.Lead, LeadId);
            reopenTarget[CrmSchema.Lead.StateCode] = new OptionSetValue(CrmSchema.Lead.State.Active);
            var reopenCtx = CreateNestedContext(CrmSchema.Entities.Lead, "Update", new ParameterCollection { { "Target", reopenTarget } });
            reopenCtx.PreEntityImages["PreImage"] = existingClosedLead;
            AssertCrmError(CrmError.CRM005, () => RunPlugin(reopenCtx, caller));

            // 7. Raw Qualified / result-link PATCH -> CRM005; Assign -> CRM018; Delete -> CRM017
            var rawQualify = new Entity(CrmSchema.Entities.Lead, LeadId);
            rawQualify[CrmSchema.Lead.StatusCode] = new OptionSetValue(CrmSchema.Lead.Status.Qualified);
            var rawQualifyCtx = CreateNestedContext(CrmSchema.Entities.Lead, "Update", new ParameterCollection { { "Target", rawQualify } });
            rawQualifyCtx.PreEntityImages["PreImage"] = existingOpenLead;
            AssertCrmError(CrmError.CRM005, () => RunPlugin(rawQualifyCtx, caller));

            AssertCrmError(CrmError.CRM018, () => RunPlugin(CreateNestedContext(CrmSchema.Entities.Lead, "Assign", new ParameterCollection()), caller));
            AssertCrmError(CrmError.CRM017, () => RunPlugin(CreateNestedContext(CrmSchema.Entities.Lead, "Delete", new ParameterCollection()), caller));

            // 8. Verified nested EXT10 Lead qualification closure -> ALLOWED
            var ext10Parent = CreateApiParent(CrmSchema.Apis.QualifyLead, CrmSchema.Entities.Lead, LeadId);
            var ext10LeadClose = new Entity(CrmSchema.Entities.Lead, LeadId);
            ext10LeadClose[CrmSchema.Lead.StateCode] = new OptionSetValue(CrmSchema.Lead.State.Inactive);
            ext10LeadClose[CrmSchema.Lead.StatusCode] = new OptionSetValue(CrmSchema.Lead.Status.Qualified);
            ext10LeadClose[CrmSchema.Lead.Account] = new EntityReference(CrmSchema.Entities.Account, AccountId);
            ext10LeadClose[CrmSchema.Lead.Contact] = new EntityReference(CrmSchema.Entities.Contact, ContactId);
            ext10LeadClose[CrmSchema.Lead.Opportunity] = new EntityReference(CrmSchema.Entities.Opportunity, OppId);
            ext10LeadClose[CrmSchema.Lead.QualifiedOn] = DateTime.UtcNow;
            ext10LeadClose["modifiedon"] = DateTime.UtcNow; // harmless platform attribute
            RunPlugin(CreateNestedContext(CrmSchema.Entities.Lead, "Update", new ParameterCollection { { "Target", ext10LeadClose } }, ext10Parent), caller);
        }

        [Fact]
        public void Opportunity_DirectCreateGuardsProgressStampAndTrustedApis_EnforceAllRules()
        {
            var caller = CreateBaseService();
            var expectedClose = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc);
            var followUp = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);

            // 1. Direct Create & CreateMultiple -> CRM005
            var validNewOpp = new Entity(CrmSchema.Entities.Opportunity, OppId);
            validNewOpp[CrmSchema.Opportunity.OriginatingLead] = new EntityReference(CrmSchema.Entities.Lead, LeadId);
            validNewOpp[CrmSchema.Opportunity.Stage] = new OptionSetValue(CrmSchema.Opportunity.StageValues.Qualify);
            validNewOpp[CrmSchema.Opportunity.Probability] = 10;
            validNewOpp[CrmSchema.Opportunity.Total] = new Money(0m);
            validNewOpp[CrmSchema.Opportunity.MaxDiscount] = 0m;
            validNewOpp[CrmSchema.Opportunity.CommandSequence] = 0;
            validNewOpp[CrmSchema.Opportunity.TransactionCurrencyId] = new EntityReference(CrmSchema.Entities.TransactionCurrency, BaseCurrencyId);

            AssertCrmError(CrmError.CRM005, () => RunPlugin(
                CreateNestedContext(CrmSchema.Entities.Opportunity, "Create", new ParameterCollection { { "Target", validNewOpp } }),
                caller));
            AssertCrmError(CrmError.CRM005, () => RunPlugin(
                CreateNestedContext(CrmSchema.Entities.Opportunity, "CreateMultiple", new ParameterCollection { { "Targets", new EntityCollection(new[] { validNewOpp }) } }),
                caller));

            // 2. Verified nested EXT10 Create -> ALLOWED
            var ext10Parent = CreateApiParent(CrmSchema.Apis.QualifyLead, CrmSchema.Entities.Lead, LeadId);
            RunPlugin(CreateNestedContext(CrmSchema.Entities.Opportunity, "Create", new ParameterCollection { { "Target", validNewOpp } }, ext10Parent), caller);

            // 3. Ordinary Open vn_decisionmaker save -> ALLOWED & stamps vn_lastprogresson ONLY when value changes
            var existingDevelopOpp = new Entity(CrmSchema.Entities.Opportunity, OppId);
            existingDevelopOpp[CrmSchema.Opportunity.StateCode] = new OptionSetValue(CrmSchema.Opportunity.State.Active);
            existingDevelopOpp[CrmSchema.Opportunity.StatusCode] = new OptionSetValue(CrmSchema.Opportunity.Status.Open);
            existingDevelopOpp[CrmSchema.Opportunity.Stage] = new OptionSetValue(CrmSchema.Opportunity.StageValues.Develop);
            existingDevelopOpp[CrmSchema.Opportunity.Name] = "Acme CRM Expansion";
            existingDevelopOpp[CrmSchema.Opportunity.Need] = "Unified sales governance";
            existingDevelopOpp[CrmSchema.Opportunity.DecisionMaker] = "VP Operations";
            existingDevelopOpp[CrmSchema.Opportunity.NextStep] = "Finalize quote";
            existingDevelopOpp[CrmSchema.Opportunity.ExpectedCloseDate] = expectedClose;
            existingDevelopOpp[CrmSchema.Opportunity.NextFollowUpDate] = followUp;
            existingDevelopOpp[CrmSchema.Opportunity.TransactionCurrencyId] = new EntityReference(CrmSchema.Entities.TransactionCurrency, BaseCurrencyId);
            existingDevelopOpp[CrmSchema.Opportunity.OwnerId] = new EntityReference(CrmSchema.Entities.SystemUser, CallerUserId);

            var dmChangeTarget = new Entity(CrmSchema.Entities.Opportunity, OppId);
            dmChangeTarget[CrmSchema.Opportunity.DecisionMaker] = "Chief Revenue Officer";
            var dmChangeCtx = CreateNestedContext(CrmSchema.Entities.Opportunity, "Update", new ParameterCollection { { "Target", dmChangeTarget } });
            dmChangeCtx.PreEntityImages["PreImage"] = existingDevelopOpp;
            RunPlugin(dmChangeCtx, caller);
            Assert.True(dmChangeTarget.Contains(CrmSchema.Opportunity.LastProgressOn));

            // Unchanged vn_decisionmaker -> does NOT stamp vn_lastprogresson
            var dmSameTarget = new Entity(CrmSchema.Entities.Opportunity, OppId);
            dmSameTarget[CrmSchema.Opportunity.DecisionMaker] = "VP Operations";
            var dmSameCtx = CreateNestedContext(CrmSchema.Entities.Opportunity, "Update", new ParameterCollection { { "Target", dmSameTarget } });
            dmSameCtx.PreEntityImages["PreImage"] = existingDevelopOpp;
            RunPlugin(dmSameCtx, caller);
            Assert.False(dmSameTarget.Contains(CrmSchema.Opportunity.LastProgressOn));

            // Changing ONLY vn_nextfollowupdate -> does NOT stamp vn_lastprogresson
            var followUpOnlyTarget = new Entity(CrmSchema.Entities.Opportunity, OppId);
            followUpOnlyTarget[CrmSchema.Opportunity.NextFollowUpDate] = followUp.AddDays(3);
            var followUpOnlyCtx = CreateNestedContext(CrmSchema.Entities.Opportunity, "Update", new ParameterCollection { { "Target", followUpOnlyTarget } });
            followUpOnlyCtx.PreEntityImages["PreImage"] = existingDevelopOpp;
            RunPlugin(followUpOnlyCtx, caller);
            Assert.False(followUpOnlyTarget.Contains(CrmSchema.Opportunity.LastProgressOn));

            // 4. Clearing vn_decisionmaker at Develop -> CRM007
            var clearDmTarget = new Entity(CrmSchema.Entities.Opportunity, OppId);
            clearDmTarget[CrmSchema.Opportunity.DecisionMaker] = "  ";
            var clearDmCtx = CreateNestedContext(CrmSchema.Entities.Opportunity, "Update", new ParameterCollection { { "Target", clearDmTarget } });
            clearDmCtx.PreEntityImages["PreImage"] = existingDevelopOpp;
            AssertCrmError(CrmError.CRM007, () => RunPlugin(clearDmCtx, caller));

            // 5. Protected fields on raw Update -> exact error codes
            AssertProtectedOppUpdate(existingDevelopOpp, CrmSchema.Opportunity.Stage, new OptionSetValue(CrmSchema.Opportunity.StageValues.Propose), CrmError.CRM006, caller);
            AssertProtectedOppUpdate(existingDevelopOpp, CrmSchema.Opportunity.Total, new Money(9999m), CrmError.CRM009, caller);
            AssertProtectedOppUpdate(existingDevelopOpp, CrmSchema.Opportunity.CurrentApproval, new EntityReference(CrmSchema.Entities.ApprovalRequest, ApprovalId), CrmError.CRM012, caller);
            AssertProtectedOppUpdate(existingDevelopOpp, CrmSchema.Opportunity.OwnerId, new EntityReference(CrmSchema.Entities.SystemUser, OtherUserId), CrmError.CRM018, caller);
            AssertProtectedOppUpdate(existingDevelopOpp, CrmSchema.Opportunity.Account, new EntityReference(CrmSchema.Entities.Account, AccountId), CrmError.CRM005, caller);

            // Closed Opportunity immutable -> CRM006
            var closedOpp = new Entity(CrmSchema.Entities.Opportunity, OppId);
            closedOpp[CrmSchema.Opportunity.StateCode] = new OptionSetValue(CrmSchema.Opportunity.State.Inactive);
            var closedEditCtx = CreateNestedContext(CrmSchema.Entities.Opportunity, "Update", new ParameterCollection { { "Target", dmChangeTarget } });
            closedEditCtx.PreEntityImages["PreImage"] = closedOpp;
            AssertCrmError(CrmError.CRM006, () => RunPlugin(closedEditCtx, caller));

            // 6. Verified nested EXT11 & EXT12 updates (with platform modifiedon & vn_total_base, plus writer-only vn_currentapproval = null)
            var ext11Parent = CreateApiParent(CrmSchema.Apis.TransitionOpportunity, CrmSchema.Entities.Opportunity, OppId, CrmSchema.Apis.Params.Command, "Advance");
            var ext11Update = new Entity(CrmSchema.Entities.Opportunity, OppId);
            ext11Update[CrmSchema.Opportunity.Stage] = new OptionSetValue(CrmSchema.Opportunity.StageValues.Propose);
            ext11Update[CrmSchema.Opportunity.Probability] = 60;
            ext11Update[CrmSchema.Opportunity.StageEnteredOn] = DateTime.UtcNow;
            ext11Update[CrmSchema.Opportunity.LastProgressOn] = DateTime.UtcNow;
            ext11Update["modifiedon"] = DateTime.UtcNow;
            RunPlugin(CreateNestedContext(CrmSchema.Entities.Opportunity, "Update", new ParameterCollection { { "Target", ext11Update } }, ext11Parent), caller);

            var ext12Parent = CreateApiParent(CrmSchema.Apis.EditOpportunityLine, CrmSchema.Entities.Opportunity, OppId, CrmSchema.Apis.Params.Operation, "Add");
            var ext12Totals = new Entity(CrmSchema.Entities.Opportunity, OppId);
            ext12Totals[CrmSchema.Opportunity.Total] = new Money(5000m);
            ext12Totals["vn_total_base"] = new Money(5000m); // allowed because vn_total is in allowlist
            ext12Totals[CrmSchema.Opportunity.MaxDiscount] = 5m;
            ext12Totals[CrmSchema.Opportunity.LastProgressOn] = DateTime.UtcNow;
            RunPlugin(CreateNestedContext(CrmSchema.Entities.Opportunity, "Update", new ParameterCollection { { "Target", ext12Totals } }, ext12Parent), caller);

            var ext12PointerClear = new Entity(CrmSchema.Entities.Opportunity, OppId);
            ext12PointerClear[CrmSchema.Opportunity.CurrentApproval] = null;
            // Non-writer clearing pointer under EXT12 -> CRM012
            AssertCrmError(CrmError.CRM012, () => RunPlugin(
                CreateNestedContext(CrmSchema.Entities.Opportunity, "Update", new ParameterCollection { { "Target", ext12PointerClear } }, ext12Parent, asWriter: false),
                caller));
            // Configured internal writer clearing pointer under EXT12 -> ALLOWED
            RunPlugin(CreateNestedContext(CrmSchema.Entities.Opportunity, "Update", new ParameterCollection { { "Target", ext12PointerClear } }, ext12Parent, asWriter: true), caller);
        }

        [Fact]
        public void Opportunity_Ext10OwnerHandoff_AllowsLeadOwnerOnlyAndBlocksWrongOwnerOrDirectAssign()
        {
            var leadOwnerId = OtherUserId;
            var wrongOwnerId = Guid.NewGuid();

            var boundLead = new Entity(CrmSchema.Entities.Lead, LeadId);
            boundLead[CrmSchema.Lead.OwnerId] = new EntityReference(CrmSchema.Entities.SystemUser, leadOwnerId);

            var boundOpp = new Entity(CrmSchema.Entities.Opportunity, OppId);
            boundOpp[CrmSchema.Opportunity.OriginatingLead] = new EntityReference(CrmSchema.Entities.Lead, LeadId);

            var caller = CreateBaseService(onRetrieve: (e, id, c) =>
            {
                if (e == CrmSchema.Entities.Lead && id == LeadId) return boundLead;
                if (e == CrmSchema.Entities.Opportunity && id == OppId) return boundOpp;
                return null;
            });

            var ext10Parent = CreateApiParent(CrmSchema.Apis.QualifyLead, CrmSchema.Entities.Lead, LeadId);

            // EXT10 Update ownerid to Lead owner -> ALLOWED
            var validOwnerUpdate = new Entity(CrmSchema.Entities.Opportunity, OppId);
            validOwnerUpdate[CrmSchema.Opportunity.OwnerId] = new EntityReference(CrmSchema.Entities.SystemUser, leadOwnerId);
            RunPlugin(
                CreateNestedContext(CrmSchema.Entities.Opportunity, "Update", new ParameterCollection { { "Target", validOwnerUpdate } }, ext10Parent),
                caller);

            // EXT10 Assign to Lead owner -> ALLOWED
            var validAssignInputs = new ParameterCollection
            {
                { "Target", new EntityReference(CrmSchema.Entities.Opportunity, OppId) },
                { "Assignee", new EntityReference(CrmSchema.Entities.SystemUser, leadOwnerId) }
            };
            RunPlugin(
                CreateNestedContext(CrmSchema.Entities.Opportunity, "Assign", validAssignInputs, ext10Parent),
                caller);

            // EXT10 Update ownerid to WRONG user -> CRM018
            var wrongOwnerUpdate = new Entity(CrmSchema.Entities.Opportunity, OppId);
            wrongOwnerUpdate[CrmSchema.Opportunity.OwnerId] = new EntityReference(CrmSchema.Entities.SystemUser, wrongOwnerId);
            AssertCrmError(CrmError.CRM018, () => RunPlugin(
                CreateNestedContext(CrmSchema.Entities.Opportunity, "Update", new ParameterCollection { { "Target", wrongOwnerUpdate } }, ext10Parent),
                caller));

            // EXT10 Assign to WRONG user -> CRM018
            var wrongAssignInputs = new ParameterCollection
            {
                { "Target", new EntityReference(CrmSchema.Entities.Opportunity, OppId) },
                { "Assignee", new EntityReference(CrmSchema.Entities.SystemUser, wrongOwnerId) }
            };
            AssertCrmError(CrmError.CRM018, () => RunPlugin(
                CreateNestedContext(CrmSchema.Entities.Opportunity, "Assign", wrongAssignInputs, ext10Parent),
                caller));

            // Direct Assign outside EXT10 -> CRM018
            AssertCrmError(CrmError.CRM018, () => RunPlugin(
                CreateNestedContext(CrmSchema.Entities.Opportunity, "Assign", validAssignInputs, parentContext: null),
                caller));
        }

        [Fact]
        public void OpportunityLine_BlocksRawCrudAndAllowsScopedExt12AddAndRemoveOnly()
        {
            var existingLine = new Entity(CrmSchema.Entities.OpportunityLine, LineId);
            existingLine[CrmSchema.OpportunityLine.Opportunity] = new EntityReference(CrmSchema.Entities.Opportunity, OppId);
            var caller = CreateBaseService(onRetrieve: (e, id, c) =>
                e == CrmSchema.Entities.OpportunityLine && id == LineId ? existingLine : null);

            var lineTarget = new Entity(CrmSchema.Entities.OpportunityLine, LineId);
            lineTarget[CrmSchema.OpportunityLine.Opportunity] = new EntityReference(CrmSchema.Entities.Opportunity, OppId);

            // Raw Create / Update / Delete / CreateMultiple / DeleteMultiple -> CRM010
            AssertCrmError(CrmError.CRM010, () => RunPlugin(CreateNestedContext(CrmSchema.Entities.OpportunityLine, "Create", new ParameterCollection { { "Target", lineTarget } }), caller));
            AssertCrmError(CrmError.CRM010, () => RunPlugin(CreateNestedContext(CrmSchema.Entities.OpportunityLine, "Update", new ParameterCollection { { "Target", lineTarget } }), caller));
            AssertCrmError(CrmError.CRM010, () => RunPlugin(CreateNestedContext(CrmSchema.Entities.OpportunityLine, "Delete", new ParameterCollection { { "Target", new EntityReference(CrmSchema.Entities.OpportunityLine, LineId) } }), caller));
            AssertCrmError(CrmError.CRM010, () => RunPlugin(CreateNestedContext(CrmSchema.Entities.OpportunityLine, "CreateMultiple", new ParameterCollection()), caller));
            AssertCrmError(CrmError.CRM010, () => RunPlugin(CreateNestedContext(CrmSchema.Entities.OpportunityLine, "DeleteMultiple", new ParameterCollection()), caller));

            // Verified EXT12 Add for matching Opportunity -> ALLOWED
            var addParent = CreateApiParent(CrmSchema.Apis.EditOpportunityLine, CrmSchema.Entities.Opportunity, OppId, CrmSchema.Apis.Params.Operation, "Add");
            RunPlugin(CreateNestedContext(CrmSchema.Entities.OpportunityLine, "Create", new ParameterCollection { { "Target", lineTarget } }, addParent), caller);

            // EXT12 Add for WRONG parent Opportunity -> CRM010
            var wrongParentLine = new Entity(CrmSchema.Entities.OpportunityLine, LineId);
            wrongParentLine[CrmSchema.OpportunityLine.Opportunity] = new EntityReference(CrmSchema.Entities.Opportunity, OtherOppId);
            AssertCrmError(CrmError.CRM010, () => RunPlugin(
                CreateNestedContext(CrmSchema.Entities.OpportunityLine, "Create", new ParameterCollection { { "Target", wrongParentLine } }, addParent),
                caller));

            // Verified EXT12 Remove for matching Opportunity -> ALLOWED
            var removeParent = CreateApiParent(CrmSchema.Apis.EditOpportunityLine, CrmSchema.Entities.Opportunity, OppId, CrmSchema.Apis.Params.Operation, "Remove");
            RunPlugin(CreateNestedContext(CrmSchema.Entities.OpportunityLine, "Delete", new ParameterCollection { { "Target", new EntityReference(CrmSchema.Entities.OpportunityLine, LineId) } }, removeParent), caller);

            // EXT12 Remove for WRONG parent Opportunity -> CRM010
            var wrongRemoveParent = CreateApiParent(CrmSchema.Apis.EditOpportunityLine, CrmSchema.Entities.Opportunity, OtherOppId, CrmSchema.Apis.Params.Operation, "Remove");
            AssertCrmError(CrmError.CRM010, () => RunPlugin(
                CreateNestedContext(CrmSchema.Entities.OpportunityLine, "Delete", new ParameterCollection { { "Target", new EntityReference(CrmSchema.Entities.OpportunityLine, LineId) } }, wrongRemoveParent),
                caller));
        }

        [Fact]
        public void OpportunityLine_Ext12AddOwnerHandoff_AllowsOppOwnerOnlyAndBlocksWrongOwnerRemoveOrDirect()
        {
            var oppOwnerId = OtherUserId;
            var wrongOwnerId = Guid.NewGuid();

            var existingLine = new Entity(CrmSchema.Entities.OpportunityLine, LineId);
            existingLine[CrmSchema.OpportunityLine.Opportunity] = new EntityReference(CrmSchema.Entities.Opportunity, OppId);

            var boundOpp = new Entity(CrmSchema.Entities.Opportunity, OppId);
            boundOpp[CrmSchema.Opportunity.OwnerId] = new EntityReference(CrmSchema.Entities.SystemUser, oppOwnerId);

            var caller = CreateBaseService(onRetrieve: (e, id, c) =>
            {
                if (e == CrmSchema.Entities.OpportunityLine && id == LineId) return existingLine;
                if (e == CrmSchema.Entities.Opportunity && id == OppId) return boundOpp;
                return null;
            });

            var addParent = CreateApiParent(CrmSchema.Apis.EditOpportunityLine, CrmSchema.Entities.Opportunity, OppId, CrmSchema.Apis.Params.Operation, "Add");
            var removeParent = CreateApiParent(CrmSchema.Apis.EditOpportunityLine, CrmSchema.Entities.Opportunity, OppId, CrmSchema.Apis.Params.Operation, "Remove");

            // EXT12 Add Update ownerid to parent Opp owner -> ALLOWED
            var validOwnerUpdate = new Entity(CrmSchema.Entities.OpportunityLine, LineId);
            validOwnerUpdate[CrmSchema.OpportunityLine.OwnerId] = new EntityReference(CrmSchema.Entities.SystemUser, oppOwnerId);
            RunPlugin(
                CreateNestedContext(CrmSchema.Entities.OpportunityLine, "Update", new ParameterCollection { { "Target", validOwnerUpdate } }, addParent),
                caller);

            // EXT12 Add Assign to parent Opp owner -> ALLOWED
            var validAssignInputs = new ParameterCollection
            {
                { "Target", new EntityReference(CrmSchema.Entities.OpportunityLine, LineId) },
                { "Assignee", new EntityReference(CrmSchema.Entities.SystemUser, oppOwnerId) }
            };
            RunPlugin(
                CreateNestedContext(CrmSchema.Entities.OpportunityLine, "Assign", validAssignInputs, addParent),
                caller);

            // Wrong owner -> CRM010
            var wrongOwnerUpdate = new Entity(CrmSchema.Entities.OpportunityLine, LineId);
            wrongOwnerUpdate[CrmSchema.OpportunityLine.OwnerId] = new EntityReference(CrmSchema.Entities.SystemUser, wrongOwnerId);
            AssertCrmError(CrmError.CRM010, () => RunPlugin(
                CreateNestedContext(CrmSchema.Entities.OpportunityLine, "Update", new ParameterCollection { { "Target", wrongOwnerUpdate } }, addParent),
                caller));

            // Remove-scoped Update/Assign -> CRM010
            AssertCrmError(CrmError.CRM010, () => RunPlugin(
                CreateNestedContext(CrmSchema.Entities.OpportunityLine, "Update", new ParameterCollection { { "Target", validOwnerUpdate } }, removeParent),
                caller));
            AssertCrmError(CrmError.CRM010, () => RunPlugin(
                CreateNestedContext(CrmSchema.Entities.OpportunityLine, "Assign", validAssignInputs, removeParent),
                caller));

            // Direct Update/Assign outside EXT12 -> CRM010
            AssertCrmError(CrmError.CRM010, () => RunPlugin(
                CreateNestedContext(CrmSchema.Entities.OpportunityLine, "Update", new ParameterCollection { { "Target", validOwnerUpdate } }, parentContext: null),
                caller));
            AssertCrmError(CrmError.CRM010, () => RunPlugin(
                CreateNestedContext(CrmSchema.Entities.OpportunityLine, "Assign", validAssignInputs, parentContext: null),
                caller));
        }

        [Fact]
        public void ApprovalRequest_BlocksUnscopedAndRejectedMutationsAndAllowsExt12Supersede()
        {
            var pendingReq = new Entity(CrmSchema.Entities.ApprovalRequest, ApprovalId);
            pendingReq[CrmSchema.ApprovalRequest.Opportunity] = new EntityReference(CrmSchema.Entities.Opportunity, OppId);
            pendingReq[CrmSchema.ApprovalRequest.Result] = new OptionSetValue(CrmSchema.ApprovalRequest.ResultValues.Pending);

            var rejectedReq = new Entity(CrmSchema.Entities.ApprovalRequest, ApprovalId);
            rejectedReq[CrmSchema.ApprovalRequest.Opportunity] = new EntityReference(CrmSchema.Entities.Opportunity, OppId);
            rejectedReq[CrmSchema.ApprovalRequest.Result] = new OptionSetValue(CrmSchema.ApprovalRequest.ResultValues.Rejected);

            var caller = CreateBaseService();
            var writerPending = CreateBaseService(onRetrieve: (e, id, c) => e == CrmSchema.Entities.ApprovalRequest ? pendingReq : null);
            var writerRejected = CreateBaseService(onRetrieve: (e, id, c) => e == CrmSchema.Entities.ApprovalRequest ? rejectedReq : null);

            var supersedePatch = new Entity(CrmSchema.Entities.ApprovalRequest, ApprovalId);
            supersedePatch[CrmSchema.ApprovalRequest.Result] = new OptionSetValue(CrmSchema.ApprovalRequest.ResultValues.Superseded);

            // Business user raw Update -> CRM013; Delete -> CRM017
            AssertCrmError(CrmError.CRM013, () => RunPlugin(
                CreateNestedContext(CrmSchema.Entities.ApprovalRequest, "Update", new ParameterCollection { { "Target", supersedePatch } }, asWriter: false),
                caller, writerPending));
            AssertCrmError(CrmError.CRM017, () => RunPlugin(
                CreateNestedContext(CrmSchema.Entities.ApprovalRequest, "Delete", new ParameterCollection { { "Target", new EntityReference(CrmSchema.Entities.ApprovalRequest, ApprovalId) } }, asWriter: true),
                caller, writerPending));

            // Unscoped Internal Writer Update (no recognized API parent) -> CRM013
            AssertCrmError(CrmError.CRM013, () => RunPlugin(
                CreateNestedContext(CrmSchema.Entities.ApprovalRequest, "Update", new ParameterCollection { { "Target", supersedePatch } }, parentContext: null, asWriter: true),
                caller, writerPending));

            // Verified Internal Writer EXT12 Supersede on Pending -> ALLOWED
            var ext12Parent = CreateApiParent(CrmSchema.Apis.EditOpportunityLine, CrmSchema.Entities.Opportunity, OppId, CrmSchema.Apis.Params.Operation, "Add");
            RunPlugin(
                CreateNestedContext(CrmSchema.Entities.ApprovalRequest, "Update", new ParameterCollection { { "Target", supersedePatch } }, ext12Parent, asWriter: true),
                caller, writerPending);

            // Verified Internal Writer EXT12 Supersede attempting to mutate Rejected -> CRM013
            AssertCrmError(CrmError.CRM013, () => RunPlugin(
                CreateNestedContext(CrmSchema.Entities.ApprovalRequest, "Update", new ParameterCollection { { "Target", supersedePatch } }, ext12Parent, asWriter: true),
                caller, writerRejected));

            // Verified Internal Writer EXT12 trying to set Approved instead of Superseded -> CRM013
            var forgeApproved = new Entity(CrmSchema.Entities.ApprovalRequest, ApprovalId);
            forgeApproved[CrmSchema.ApprovalRequest.Result] = new OptionSetValue(CrmSchema.ApprovalRequest.ResultValues.Approved);
            AssertCrmError(CrmError.CRM013, () => RunPlugin(
                CreateNestedContext(CrmSchema.Entities.ApprovalRequest, "Update", new ParameterCollection { { "Target", forgeApproved } }, ext12Parent, asWriter: true),
                caller, writerPending));
        }

        [Fact]
        public void OpportunitySalesProcess_BlocksDirectAndUnscopedWritesAndAllowsExt10AndExt11ByWriter()
        {
            var oppRecord = new Entity(CrmSchema.Entities.Opportunity, OppId);
            oppRecord[CrmSchema.Opportunity.OriginatingLead] = new EntityReference(CrmSchema.Entities.Lead, LeadId);

            var existingBpf = new Entity(CrmSchema.Entities.OpportunitySalesProcess, BpfId);
            existingBpf[CrmSchema.Bpf.Opportunity] = new EntityReference(CrmSchema.Entities.Opportunity, OppId);

            var caller = CreateBaseService();
            var writerSvc = CreateBaseService(onRetrieve: (e, id, c) =>
            {
                if (e == CrmSchema.Entities.Opportunity && id == OppId) return oppRecord;
                if (e == CrmSchema.Entities.OpportunitySalesProcess && id == BpfId) return existingBpf;
                return null;
            });

            var bpfCreate = new Entity(CrmSchema.Entities.OpportunitySalesProcess, BpfId);
            bpfCreate[CrmSchema.Bpf.Name] = "Acme Deal";
            bpfCreate[CrmSchema.Bpf.Opportunity] = new EntityReference(CrmSchema.Entities.Opportunity, OppId);
            bpfCreate[CrmSchema.Bpf.ProcessId] = new EntityReference(CrmSchema.Entities.Workflow, CrmSchema.Bpf.WorkflowId);
            bpfCreate[CrmSchema.Bpf.ActiveStageId] = new EntityReference(CrmSchema.Entities.ProcessStage, CrmSchema.Bpf.StageQualifyId);
            bpfCreate[CrmSchema.Bpf.TraversedPath] = CrmSchema.Bpf.StageQualifyId.ToString("D");

            var bpfAdvance = new Entity(CrmSchema.Entities.OpportunitySalesProcess, BpfId);
            bpfAdvance[CrmSchema.Bpf.ActiveStageId] = new EntityReference(CrmSchema.Entities.ProcessStage, CrmSchema.Bpf.StageDevelopId);
            bpfAdvance[CrmSchema.Bpf.TraversedPath] = CrmSchema.Bpf.StageQualifyId.ToString("D") + "," + CrmSchema.Bpf.StageDevelopId.ToString("D");

            // Direct user Create/Update/Delete -> CRM006
            AssertCrmError(CrmError.CRM006, () => RunPlugin(CreateNestedContext(CrmSchema.Entities.OpportunitySalesProcess, "Create", new ParameterCollection { { "Target", bpfCreate } }, asWriter: false), caller, writerSvc));
            AssertCrmError(CrmError.CRM006, () => RunPlugin(CreateNestedContext(CrmSchema.Entities.OpportunitySalesProcess, "Update", new ParameterCollection { { "Target", bpfAdvance } }, asWriter: false), caller, writerSvc));
            AssertCrmError(CrmError.CRM006, () => RunPlugin(CreateNestedContext(CrmSchema.Entities.OpportunitySalesProcess, "Delete", new ParameterCollection { { "Target", new EntityReference(CrmSchema.Entities.OpportunitySalesProcess, BpfId) } }, asWriter: true), caller, writerSvc));

            // Unscoped Internal Writer Create/Update -> CRM006
            AssertCrmError(CrmError.CRM006, () => RunPlugin(CreateNestedContext(CrmSchema.Entities.OpportunitySalesProcess, "Create", new ParameterCollection { { "Target", bpfCreate } }, parentContext: null, asWriter: true), caller, writerSvc));
            AssertCrmError(CrmError.CRM006, () => RunPlugin(CreateNestedContext(CrmSchema.Entities.OpportunitySalesProcess, "Update", new ParameterCollection { { "Target", bpfAdvance } }, parentContext: null, asWriter: true), caller, writerSvc));

            // Verified Internal Writer EXT10 Create -> ALLOWED
            var ext10Parent = CreateApiParent(CrmSchema.Apis.QualifyLead, CrmSchema.Entities.Lead, LeadId);
            RunPlugin(CreateNestedContext(CrmSchema.Entities.OpportunitySalesProcess, "Create", new ParameterCollection { { "Target", bpfCreate } }, ext10Parent, asWriter: true), caller, writerSvc);

            // Verified Internal Writer EXT11 Update for matching Opportunity -> ALLOWED
            var ext11Parent = CreateApiParent(CrmSchema.Apis.TransitionOpportunity, CrmSchema.Entities.Opportunity, OppId, CrmSchema.Apis.Params.Command, "Advance");
            RunPlugin(CreateNestedContext(CrmSchema.Entities.OpportunitySalesProcess, "Update", new ParameterCollection { { "Target", bpfAdvance } }, ext11Parent, asWriter: true), caller, writerSvc);

            // Verified Internal Writer EXT11 Update for WRONG Opportunity -> CRM006
            var wrongExt11Parent = CreateApiParent(CrmSchema.Apis.TransitionOpportunity, CrmSchema.Entities.Opportunity, OtherOppId, CrmSchema.Apis.Params.Command, "Advance");
            AssertCrmError(CrmError.CRM006, () => RunPlugin(
                CreateNestedContext(CrmSchema.Entities.OpportunitySalesProcess, "Update", new ParameterCollection { { "Target", bpfAdvance } }, wrongExt11Parent, asWriter: true),
                caller, writerSvc));
        }

        [Fact]
        public void PlatformStateSplit_AllowsValidatedChildUpdateAndSetStateWhileBlockingDirectSetState()
        {
            var caller = CreateBaseService();
            var writerSvc = CreateBaseService();

            // 1. Direct SetState on Lead, Opportunity, and BPF must still fail closed
            var leadSetStateInputs = new ParameterCollection
            {
                { "EntityMoniker", new EntityReference(CrmSchema.Entities.Lead, LeadId) },
                { "State", new OptionSetValue(CrmSchema.Lead.State.Inactive) },
                { "Status", new OptionSetValue(CrmSchema.Lead.Status.Disqualified) }
            };
            AssertCrmError(CrmError.CRM005, () => RunPlugin(
                CreateNestedContext(CrmSchema.Entities.Lead, "SetState", leadSetStateInputs, parentContext: null),
                caller));

            var oppSetStateInputs = new ParameterCollection
            {
                { "EntityMoniker", new EntityReference(CrmSchema.Entities.Opportunity, OppId) },
                { "State", new OptionSetValue(CrmSchema.Opportunity.State.Inactive) },
                { "Status", new OptionSetValue(CrmSchema.Opportunity.Status.Won) }
            };
            AssertCrmError(CrmError.CRM006, () => RunPlugin(
                CreateNestedContext(CrmSchema.Entities.Opportunity, "SetState", oppSetStateInputs, parentContext: null),
                caller));

            var bpfSetStateInputs = new ParameterCollection
            {
                { "EntityMoniker", new EntityReference(CrmSchema.Entities.OpportunitySalesProcess, BpfId) },
                { "State", new OptionSetValue(CrmSchema.Bpf.StateInactive) },
                { "Status", new OptionSetValue(CrmSchema.Bpf.StatusFinished) }
            };
            AssertCrmError(CrmError.CRM006, () => RunPlugin(
                CreateNestedContext(CrmSchema.Entities.OpportunitySalesProcess, "SetState", bpfSetStateInputs, parentContext: null, asWriter: true),
                caller, writerSvc));

            // 2. Validated Lead Disqualification Stage-30 Update spawning child Update + SetState + SetStateDynamicEntity -> ALLOWED
            var leadDisqParent = new FakePluginExecutionContext
            {
                PrimaryEntityName = CrmSchema.Entities.Lead,
                PrimaryEntityId = LeadId,
                MessageName = "Update",
                Stage = 30,
                Mode = 0,
                IsInTransaction = true,
                Depth = 1,
                CorrelationId = CorrelationId,
                InitiatingUserId = CallerUserId,
                UserId = CallerUserId
            };

            var childDisqUpdate = new Entity(CrmSchema.Entities.Lead, LeadId);
            childDisqUpdate[CrmSchema.Lead.DisqualificationReason] = new OptionSetValue(CrmSchema.Lead.DisqualificationReasonValues.NoFit);
            childDisqUpdate[CrmSchema.Lead.DisqualificationComments] = "No fit";
            childDisqUpdate[CrmSchema.Lead.DisqualifiedOn] = DateTime.UtcNow;

            RunPlugin(
                CreateNestedContext(CrmSchema.Entities.Lead, "Update", new ParameterCollection { { "Target", childDisqUpdate } }, leadDisqParent),
                caller);
            RunPlugin(
                CreateNestedContext(CrmSchema.Entities.Lead, "SetState", leadSetStateInputs, leadDisqParent),
                caller);

            var leadSetStateStage30 = new FakePluginExecutionContext
            {
                PrimaryEntityName = CrmSchema.Entities.Lead,
                PrimaryEntityId = LeadId,
                MessageName = "SetState",
                Stage = 30,
                Mode = 0,
                IsInTransaction = true,
                Depth = 2,
                CorrelationId = CorrelationId,
                InitiatingUserId = CallerUserId,
                UserId = CallerUserId,
                ParentContext = leadDisqParent
            };
            var dynSetStateCtx = CreateNestedContext(CrmSchema.Entities.Lead, "SetStateDynamicEntity", leadSetStateInputs, leadSetStateStage30);
            dynSetStateCtx.Depth = 3;
            RunPlugin(dynSetStateCtx, caller);

            // Conversion fields on Disqualification child Update must still be rejected -> CRM005
            var illegalChildConv = new Entity(CrmSchema.Entities.Lead, LeadId);
            illegalChildConv[CrmSchema.Lead.Account] = new EntityReference(CrmSchema.Entities.Account, AccountId);
            AssertCrmError(CrmError.CRM005, () => RunPlugin(
                CreateNestedContext(CrmSchema.Entities.Lead, "Update", new ParameterCollection { { "Target", illegalChildConv } }, leadDisqParent),
                caller));

            // 3. EXT10 Lead Qualification Stage-30 Update spawning stripped child Update + SetState -> ALLOWED
            var ext10Parent = CreateApiParent(CrmSchema.Apis.QualifyLead, CrmSchema.Entities.Lead, LeadId);
            var ext10LeadUpdateStage30 = new FakePluginExecutionContext
            {
                PrimaryEntityName = CrmSchema.Entities.Lead,
                PrimaryEntityId = LeadId,
                MessageName = "Update",
                Stage = 30,
                Mode = 0,
                IsInTransaction = true,
                Depth = 2,
                CorrelationId = CorrelationId,
                InitiatingUserId = CallerUserId,
                UserId = CallerUserId,
                ParentContext = ext10Parent
            };
            var strippedExt10LeadChild = new Entity(CrmSchema.Entities.Lead, LeadId);
            strippedExt10LeadChild[CrmSchema.Lead.Account] = new EntityReference(CrmSchema.Entities.Account, AccountId);
            strippedExt10LeadChild[CrmSchema.Lead.Contact] = new EntityReference(CrmSchema.Entities.Contact, ContactId);
            strippedExt10LeadChild[CrmSchema.Lead.Opportunity] = new EntityReference(CrmSchema.Entities.Opportunity, OppId);
            strippedExt10LeadChild[CrmSchema.Lead.QualifiedOn] = DateTime.UtcNow;

            var ext10ChildCtx = CreateNestedContext(CrmSchema.Entities.Lead, "Update", new ParameterCollection { { "Target", strippedExt10LeadChild } }, ext10LeadUpdateStage30);
            ext10ChildCtx.Depth = 3;
            RunPlugin(ext10ChildCtx, caller);

            // 4. EXT11 Opportunity & BPF Close Stage-30 Updates spawning child SetState -> ALLOWED
            var ext11Parent = CreateApiParent(CrmSchema.Apis.TransitionOpportunity, CrmSchema.Entities.Opportunity, OppId, CrmSchema.Apis.Params.Command, "Won");
            var ext11OppUpdateStage30 = new FakePluginExecutionContext
            {
                PrimaryEntityName = CrmSchema.Entities.Opportunity,
                PrimaryEntityId = OppId,
                MessageName = "Update",
                Stage = 30,
                Mode = 0,
                IsInTransaction = true,
                Depth = 2,
                CorrelationId = CorrelationId,
                InitiatingUserId = CallerUserId,
                UserId = CallerUserId,
                ParentContext = ext11Parent
            };
            var oppChildSetStateCtx = CreateNestedContext(CrmSchema.Entities.Opportunity, "SetState", oppSetStateInputs, ext11OppUpdateStage30);
            oppChildSetStateCtx.Depth = 3;
            RunPlugin(oppChildSetStateCtx, caller);

            var ext11BpfUpdateStage30 = new FakePluginExecutionContext
            {
                PrimaryEntityName = CrmSchema.Entities.OpportunitySalesProcess,
                PrimaryEntityId = BpfId,
                MessageName = "Update",
                Stage = 30,
                Mode = 0,
                IsInTransaction = true,
                Depth = 2,
                CorrelationId = CorrelationId,
                InitiatingUserId = CallerUserId,
                UserId = WriterUserId,
                ParentContext = ext11Parent
            };
            var bpfChildSetStateCtx = CreateNestedContext(CrmSchema.Entities.OpportunitySalesProcess, "SetState", bpfSetStateInputs, ext11BpfUpdateStage30, asWriter: true);
            bpfChildSetStateCtx.Depth = 3;
            RunPlugin(bpfChildSetStateCtx, caller, writerSvc);
        }

        [Fact]
        public void PlatformMultipleDelegation_AllowsSingleItemChainedCallsAndBlocksForgedOrMultiItemCalls()
        {
            var followUp = new DateTime(2026, 11, 25, 0, 0, 0, DateTimeKind.Utc);
            var expectedClose = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc);

            var existingOpenLead = new Entity(CrmSchema.Entities.Lead, LeadId);
            existingOpenLead[CrmSchema.Lead.StateCode] = new OptionSetValue(CrmSchema.Lead.State.Active);
            existingOpenLead[CrmSchema.Lead.StatusCode] = new OptionSetValue(CrmSchema.Lead.Status.New);
            existingOpenLead[CrmSchema.Lead.OwnerId] = new EntityReference(CrmSchema.Entities.SystemUser, CallerUserId);
            existingOpenLead[CrmSchema.Lead.Name] = "Delegated Lead";
            existingOpenLead[CrmSchema.Lead.LastName] = "Alpha";
            existingOpenLead[CrmSchema.Lead.Source] = new OptionSetValue(CrmSchema.Lead.SourceValues.Referral);
            existingOpenLead[CrmSchema.Lead.Rating] = new OptionSetValue(CrmSchema.Lead.RatingValues.Warm);
            existingOpenLead[CrmSchema.Lead.Email] = "alpha@contoso.example";
            existingOpenLead[CrmSchema.Lead.NextFollowUpDate] = followUp;
            existingOpenLead[CrmSchema.Lead.TransactionCurrencyId] = new EntityReference(CrmSchema.Entities.TransactionCurrency, BaseCurrencyId);

            var existingOpp = new Entity(CrmSchema.Entities.Opportunity, OppId);
            existingOpp[CrmSchema.Opportunity.StateCode] = new OptionSetValue(CrmSchema.Opportunity.State.Active);
            existingOpp[CrmSchema.Opportunity.StatusCode] = new OptionSetValue(CrmSchema.Opportunity.Status.Open);
            existingOpp[CrmSchema.Opportunity.Stage] = new OptionSetValue(CrmSchema.Opportunity.StageValues.Qualify);
            existingOpp[CrmSchema.Opportunity.Name] = "Delegated Opp";
            existingOpp[CrmSchema.Opportunity.Need] = "CRM";
            existingOpp[CrmSchema.Opportunity.NextStep] = "Follow up";
            existingOpp[CrmSchema.Opportunity.ExpectedCloseDate] = expectedClose;
            existingOpp[CrmSchema.Opportunity.NextFollowUpDate] = followUp;
            existingOpp[CrmSchema.Opportunity.TransactionCurrencyId] = new EntityReference(CrmSchema.Entities.TransactionCurrency, BaseCurrencyId);
            existingOpp[CrmSchema.Opportunity.OwnerId] = new EntityReference(CrmSchema.Entities.SystemUser, CallerUserId);
            existingOpp[CrmSchema.Opportunity.OriginatingLead] = new EntityReference(CrmSchema.Entities.Lead, LeadId);

            var existingBpf = new Entity(CrmSchema.Entities.OpportunitySalesProcess, BpfId);
            existingBpf[CrmSchema.Bpf.Opportunity] = new EntityReference(CrmSchema.Entities.Opportunity, OppId);

            var caller = CreateBaseService(onRetrieve: (e, id, c) =>
            {
                if (e == CrmSchema.Entities.Lead && id == LeadId) return existingOpenLead;
                if (e == CrmSchema.Entities.Opportunity && id == OppId) return existingOpp;
                return null;
            });
            var writerSvc = CreateBaseService(onRetrieve: (e, id, c) =>
            {
                if (e == CrmSchema.Entities.Opportunity && id == OppId) return existingOpp;
                if (e == CrmSchema.Entities.OpportunitySalesProcess && id == BpfId) return existingBpf;
                return null;
            });

            // 1. Lead Update -> delegated 1-item UpdateMultiple for Disqualification succeeds
            var disqTarget = new Entity(CrmSchema.Entities.Lead, LeadId);
            disqTarget[CrmSchema.Lead.StateCode] = new OptionSetValue(CrmSchema.Lead.State.Inactive);
            disqTarget[CrmSchema.Lead.StatusCode] = new OptionSetValue(CrmSchema.Lead.Status.Disqualified);
            disqTarget[CrmSchema.Lead.DisqualificationReason] = new OptionSetValue(CrmSchema.Lead.DisqualificationReasonValues.NoFit);
            disqTarget[CrmSchema.Lead.DisqualificationComments] = "No fit";

            RunPlugin(CreateNestedContext(CrmSchema.Entities.Lead, "Update", new ParameterCollection { { "Target", disqTarget } }), caller);
            RunPlugin(CreateNestedContext(CrmSchema.Entities.Lead, "UpdateMultiple", new ParameterCollection { { "Targets", new EntityCollection(new[] { disqTarget }) } }), caller);

            // SetStateDynamicEntity spawned at Depth=2 from UpdateMultiple Stage-30 parent succeeds
            var updateMultipleStage30Parent = new FakePluginExecutionContext
            {
                PrimaryEntityName = CrmSchema.Entities.Lead,
                PrimaryEntityId = Guid.Empty,
                MessageName = "UpdateMultiple",
                Stage = 30,
                Mode = 0,
                IsInTransaction = true,
                Depth = 1,
                CorrelationId = CorrelationId,
                InitiatingUserId = CallerUserId,
                UserId = CallerUserId,
                InputParameters = new ParameterCollection { { "Targets", new EntityCollection(new[] { disqTarget }) } }
            };
            var leadSetStateDynInputs = new ParameterCollection
            {
                { "EntityMoniker", new EntityReference(CrmSchema.Entities.Lead, LeadId) },
                { "State", "Inactive" },
                { "Status", CrmSchema.Lead.Status.Disqualified }
            };
            RunPlugin(CreateNestedContext(CrmSchema.Entities.Lead, "SetStateDynamicEntity", leadSetStateDynInputs, updateMultipleStage30Parent), caller);

            // OData PATCH Upsert Stage-30 parent (PrimaryEntityName = "none", Target = Entity("vn_lead", LeadId)) also succeeds
            var odataUpsertParent = new FakePluginExecutionContext
            {
                PrimaryEntityName = "none",
                PrimaryEntityId = Guid.Empty,
                MessageName = "Upsert",
                Stage = 30,
                Mode = 0,
                IsInTransaction = true,
                Depth = 1,
                CorrelationId = CorrelationId,
                InitiatingUserId = CallerUserId,
                UserId = CallerUserId,
                InputParameters = new ParameterCollection { { "Target", disqTarget } }
            };
            RunPlugin(CreateNestedContext(CrmSchema.Entities.Lead, "SetStateDynamicEntity", leadSetStateDynInputs, odataUpsertParent), caller);

            // Direct Update with pre-populated vn_disqualifiedon still fails closed -> CRM005
            AssertCrmError(CrmError.CRM005, () => RunPlugin(
                CreateNestedContext(CrmSchema.Entities.Lead, "Update", new ParameterCollection { { "Target", disqTarget } }),
                caller));

            // 2. Opportunity Update -> delegated 1-item UpdateMultiple for progress change succeeds
            var oppEdit = new Entity(CrmSchema.Entities.Opportunity, OppId);
            oppEdit[CrmSchema.Opportunity.DecisionMaker] = "CTO Jane";
            RunPlugin(CreateNestedContext(CrmSchema.Entities.Opportunity, "Update", new ParameterCollection { { "Target", oppEdit } }), caller);
            RunPlugin(CreateNestedContext(CrmSchema.Entities.Opportunity, "UpdateMultiple", new ParameterCollection { { "Targets", new EntityCollection(new[] { oppEdit }) } }), caller);

            // Direct Update with pre-populated vn_lastprogresson still fails closed -> CRM006
            AssertCrmError(CrmError.CRM006, () => RunPlugin(
                CreateNestedContext(CrmSchema.Entities.Opportunity, "Update", new ParameterCollection { { "Target", oppEdit } }),
                caller));

            // 3. BPF 1-item CreateMultiple under EXT10 by Writer succeeds; multi-item fails closed -> CRM006
            var ext10Parent = CreateApiParent(CrmSchema.Apis.QualifyLead, CrmSchema.Entities.Lead, LeadId);
            var bpfCreate = new Entity(CrmSchema.Entities.OpportunitySalesProcess, BpfId);
            bpfCreate[CrmSchema.Bpf.Name] = "Deal BPF";
            bpfCreate[CrmSchema.Bpf.Opportunity] = new EntityReference(CrmSchema.Entities.Opportunity, OppId);
            bpfCreate[CrmSchema.Bpf.ProcessId] = new EntityReference(CrmSchema.Entities.Workflow, CrmSchema.Bpf.WorkflowId);
            bpfCreate[CrmSchema.Bpf.ActiveStageId] = new EntityReference(CrmSchema.Entities.ProcessStage, CrmSchema.Bpf.StageQualifyId);
            bpfCreate[CrmSchema.Bpf.TraversedPath] = CrmSchema.Bpf.StageQualifyId.ToString("D");
            bpfCreate["createdon"] = DateTime.UtcNow;
            bpfCreate["createdby"] = new EntityReference(CrmSchema.Entities.SystemUser, WriterUserId);
            bpfCreate["organizationid"] = new EntityReference(CrmSchema.Entities.Organization, Guid.NewGuid());
            bpfCreate["bpf_duration"] = 0;

            RunPlugin(
                CreateNestedContext(CrmSchema.Entities.OpportunitySalesProcess, "CreateMultiple", new ParameterCollection { { "Targets", new EntityCollection(new[] { bpfCreate }) } }, ext10Parent, asWriter: true),
                caller, writerSvc);
            AssertCrmError(CrmError.CRM006, () => RunPlugin(
                CreateNestedContext(CrmSchema.Entities.OpportunitySalesProcess, "CreateMultiple", new ParameterCollection { { "Targets", new EntityCollection(new[] { bpfCreate, bpfCreate }) } }, ext10Parent, asWriter: true),
                caller, writerSvc));

            // 4. SandboxFabric gRPC UpdateMultiple parent without serialized Targets under EXT10 -> SetStateDynamicEntity at Depth=3 succeeds
            var grpcUpdateMultipleUnderExt10 = new FakePluginExecutionContext
            {
                PrimaryEntityName = CrmSchema.Entities.Lead,
                PrimaryEntityId = Guid.Empty,
                MessageName = "UpdateMultiple",
                Stage = 30,
                Mode = 0,
                IsInTransaction = true,
                Depth = 2,
                CorrelationId = CorrelationId,
                InitiatingUserId = CallerUserId,
                UserId = CallerUserId,
                ParentContext = ext10Parent,
                InputParameters = new ParameterCollection()
            };
            var ext10SetStateDynCtx = CreateNestedContext(CrmSchema.Entities.Lead, "SetStateDynamicEntity", leadSetStateDynInputs, grpcUpdateMultipleUnderExt10);
            ext10SetStateDynCtx.Depth = 3;
            RunPlugin(ext10SetStateDynCtx, caller);
        }

        [Fact]
        public void PlatformAutoBpfCreateUnderExt10_AllowsStrictF02MatchAndBlocksAllForgedVariants()
        {
            var matchingOpp = new Entity(CrmSchema.Entities.Opportunity, OppId);
            matchingOpp[CrmSchema.Opportunity.OriginatingLead] = new EntityReference(CrmSchema.Entities.Lead, LeadId);

            var wrongLeadOpp = new Entity(CrmSchema.Entities.Opportunity, OtherOppId);
            wrongLeadOpp[CrmSchema.Opportunity.OriginatingLead] = new EntityReference(CrmSchema.Entities.Lead, OtherLeadId);

            var existingBpf = new Entity(CrmSchema.Entities.OpportunitySalesProcess, BpfId);
            existingBpf[CrmSchema.Bpf.Opportunity] = new EntityReference(CrmSchema.Entities.Opportunity, OppId);

            var caller = CreateBaseService(onRetrieve: (e, id, c) =>
            {
                if (e == CrmSchema.Entities.Opportunity && id == OppId) return matchingOpp;
                if (e == CrmSchema.Entities.Opportunity && id == OtherOppId) return wrongLeadOpp;
                if (e == CrmSchema.Entities.OpportunitySalesProcess && id == BpfId) return existingBpf;
                return null;
            });

            var ext10Parent = CreateApiParent(CrmSchema.Apis.QualifyLead, CrmSchema.Entities.Lead, LeadId);
            var oppCreateParent = new FakePluginExecutionContext
            {
                PrimaryEntityName = CrmSchema.Entities.Opportunity,
                PrimaryEntityId = OppId,
                MessageName = "Create",
                Stage = 30,
                Mode = 0,
                IsInTransaction = true,
                Depth = 2,
                CorrelationId = CorrelationId,
                InitiatingUserId = CallerUserId,
                UserId = CallerUserId,
                ParentContext = ext10Parent
            };
            var oppCreateMultipleParent = new FakePluginExecutionContext
            {
                PrimaryEntityName = CrmSchema.Entities.Opportunity,
                PrimaryEntityId = Guid.Empty,
                MessageName = "CreateMultiple",
                Stage = 30,
                Mode = 0,
                IsInTransaction = true,
                Depth = 2,
                CorrelationId = CorrelationId,
                InitiatingUserId = CallerUserId,
                UserId = CallerUserId,
                ParentContext = ext10Parent
            };

            Entity BuildValidAutoBpf(Guid oppId)
            {
                var bpf = new Entity(CrmSchema.Entities.OpportunitySalesProcess, BpfId);
                bpf[CrmSchema.Bpf.Name] = "Auto Deal BPF";
                bpf[CrmSchema.Bpf.Opportunity] = new EntityReference(CrmSchema.Entities.Opportunity, oppId);
                bpf[CrmSchema.Bpf.ProcessId] = new EntityReference(CrmSchema.Entities.Workflow, CrmSchema.Bpf.WorkflowId);
                bpf[CrmSchema.Bpf.ActiveStageId] = new EntityReference(CrmSchema.Entities.ProcessStage, CrmSchema.Bpf.StageQualifyId);
                bpf[CrmSchema.Bpf.TraversedPath] = CrmSchema.Bpf.StageQualifyId.ToString("D");
                bpf[CrmSchema.Bpf.StateCode] = new OptionSetValue(0);
                bpf[CrmSchema.Bpf.StatusCode] = new OptionSetValue(1);
                bpf["organizationid"] = new EntityReference(CrmSchema.Entities.Organization, Guid.NewGuid());
                return bpf;
            }

            // 1. Positive: Platform auto-BPF single Create nested under Create(vn_opportunity) -> vn_QualifyLead (non-writer caller) -> ALLOWED
            var autoCreateCtx = CreateNestedContext(
                CrmSchema.Entities.OpportunitySalesProcess,
                "Create",
                new ParameterCollection { { "Target", BuildValidAutoBpf(OppId) } },
                oppCreateParent,
                asWriter: false);
            autoCreateCtx.Depth = 3;
            RunPlugin(autoCreateCtx, caller);

            // 2. Positive: Platform auto-BPF 1-item CreateMultiple nested under CreateMultiple(vn_opportunity) -> vn_QualifyLead (non-writer caller) -> ALLOWED
            var autoCreateMultiCtx = CreateNestedContext(
                CrmSchema.Entities.OpportunitySalesProcess,
                "CreateMultiple",
                new ParameterCollection { { "Targets", new EntityCollection(new[] { BuildValidAutoBpf(OppId) }) } },
                oppCreateMultipleParent,
                asWriter: false);
            autoCreateMultiCtx.Depth = 3;
            RunPlugin(autoCreateMultiCtx, caller);

            // 3. Negative: Direct Admin Create outside vn_QualifyLead -> CRM006
            AssertCrmError(CrmError.CRM006, () => RunPlugin(
                CreateNestedContext(
                    CrmSchema.Entities.OpportunitySalesProcess,
                    "Create",
                    new ParameterCollection { { "Target", BuildValidAutoBpf(OppId) } },
                    parentContext: null,
                    asWriter: false),
                caller));

            // 4. Negative: Non-writer Create directly under EXT10 WITHOUT enclosing Opportunity Create -> CRM006
            AssertCrmError(CrmError.CRM006, () => RunPlugin(
                CreateNestedContext(
                    CrmSchema.Entities.OpportunitySalesProcess,
                    "Create",
                    new ParameterCollection { { "Target", BuildValidAutoBpf(OppId) } },
                    parentContext: ext10Parent,
                    asWriter: false),
                caller));

            // 5. Negative: Wrong stage (StageDevelopId) on auto-BPF Create -> CRM006
            var wrongStageBpf = BuildValidAutoBpf(OppId);
            wrongStageBpf[CrmSchema.Bpf.ActiveStageId] = new EntityReference(CrmSchema.Entities.ProcessStage, CrmSchema.Bpf.StageDevelopId);
            var wrongStageCtx = CreateNestedContext(
                CrmSchema.Entities.OpportunitySalesProcess,
                "Create",
                new ParameterCollection { { "Target", wrongStageBpf } },
                oppCreateParent,
                asWriter: false);
            wrongStageCtx.Depth = 3;
            AssertCrmError(CrmError.CRM006, () => RunPlugin(wrongStageCtx, caller));

            // 6. Negative: Wrong workflow ProcessId or Inactive state on auto-BPF Create -> CRM006
            var wrongProcessBpf = BuildValidAutoBpf(OppId);
            wrongProcessBpf[CrmSchema.Bpf.ProcessId] = new EntityReference(CrmSchema.Entities.Workflow, Guid.NewGuid());
            AssertCrmError(CrmError.CRM006, () => RunPlugin(
                CreateNestedContext(CrmSchema.Entities.OpportunitySalesProcess, "Create", new ParameterCollection { { "Target", wrongProcessBpf } }, oppCreateParent, asWriter: false),
                caller));

            var inactiveAutoBpf = BuildValidAutoBpf(OppId);
            inactiveAutoBpf[CrmSchema.Bpf.StateCode] = new OptionSetValue(1);
            AssertCrmError(CrmError.CRM006, () => RunPlugin(
                CreateNestedContext(CrmSchema.Entities.OpportunitySalesProcess, "Create", new ParameterCollection { { "Target", inactiveAutoBpf } }, oppCreateParent, asWriter: false),
                caller));

            // 7. Negative: Wrong-Lead Opportunity referenced by auto-BPF -> CRM006
            AssertCrmError(CrmError.CRM006, () => RunPlugin(
                CreateNestedContext(CrmSchema.Entities.OpportunitySalesProcess, "Create", new ParameterCollection { { "Target", BuildValidAutoBpf(OtherOppId) } }, oppCreateParent, asWriter: false),
                caller));

            // 8. Negative: Multi-item CreateMultiple (2 items) even under Create(vn_opportunity) -> EXT10 -> CRM006
            AssertCrmError(CrmError.CRM006, () => RunPlugin(
                CreateNestedContext(
                    CrmSchema.Entities.OpportunitySalesProcess,
                    "CreateMultiple",
                    new ParameterCollection { { "Targets", new EntityCollection(new[] { BuildValidAutoBpf(OppId), BuildValidAutoBpf(OppId) }) } },
                    oppCreateParent,
                    asWriter: false),
                caller));

            // 9. Negative: Non-writer Update on BPF (under EXT10 or EXT11) -> CRM006
            var bpfUpdate = new Entity(CrmSchema.Entities.OpportunitySalesProcess, BpfId);
            bpfUpdate[CrmSchema.Bpf.ActiveStageId] = new EntityReference(CrmSchema.Entities.ProcessStage, CrmSchema.Bpf.StageDevelopId);
            AssertCrmError(CrmError.CRM006, () => RunPlugin(
                CreateNestedContext(CrmSchema.Entities.OpportunitySalesProcess, "Update", new ParameterCollection { { "Target", bpfUpdate } }, oppCreateParent, asWriter: false),
                caller));

            var ext11Parent = CreateApiParent(CrmSchema.Apis.TransitionOpportunity, CrmSchema.Entities.Opportunity, OppId, CrmSchema.Apis.Params.Command, "Advance");
            AssertCrmError(CrmError.CRM006, () => RunPlugin(
                CreateNestedContext(CrmSchema.Entities.OpportunitySalesProcess, "Update", new ParameterCollection { { "Target", bpfUpdate } }, ext11Parent, asWriter: false),
                caller));
        }

        private static void AssertProtectedOppUpdate(
            Entity preImage,
            string attributeName,
            object value,
            string expectedErrorCode,
            IOrganizationService caller)
        {
            var patch = new Entity(CrmSchema.Entities.Opportunity, OppId);
            patch[attributeName] = value;
            var ctx = CreateNestedContext(CrmSchema.Entities.Opportunity, "Update", new ParameterCollection { { "Target", patch } });
            ctx.PreEntityImages["PreImage"] = preImage;
            AssertCrmError(expectedErrorCode, () => RunPlugin(ctx, caller));
        }
    }
}
