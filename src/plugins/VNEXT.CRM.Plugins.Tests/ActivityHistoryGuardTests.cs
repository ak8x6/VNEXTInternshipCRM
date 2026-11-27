using System;
using System.Collections.Generic;
using System.ServiceModel;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using VNEXT.CRM.Plugins.Core;
using VNEXT.CRM.Plugins.Guards;
using Xunit;

namespace VNEXT.CRM.Plugins.Tests
{
    public class ActivityHistoryGuardTests
    {
        private static readonly Guid CorrelationId = new Guid("c1c1c1c1-c1c1-c1c1-c1c1-c1c1c1c1c1c1");
        private static readonly Guid CallerUserId = new Guid("c2c2c2c2-c2c2-c2c2-c2c2-c2c2c2c2c2c2");
        private static readonly Guid OtherUserId = new Guid("c3c3c3c3-c3c3-c3c3-c3c3-c3c3c3c3c3c3");
        private static readonly Guid LeadId = new Guid("d1d1d1d1-d1d1-d1d1-d1d1-d1d1d1d1d1d1");
        private static readonly Guid ClosedWonOppId = new Guid("d2d2d2d2-d2d2-d2d2-d2d2-d2d2d2d2d2d2");
        private static readonly Guid AccountId = new Guid("d3d3d3d3-d3d3-d3d3-d3d3-d3d3d3d3d3d3");
        private static readonly Guid ContactId = new Guid("d4d4d4d4-d4d4-d4d4-d4d4-d4d4d4d4d4d4");
        private static readonly Guid UnauthorizedContactId = new Guid("d5d5d5d5-d5d5-d5d5-d5d5-d5d5d5d5d5d5");
        private static readonly Guid TaskId = new Guid("e1e1e1e1-e1e1-e1e1-e1e1-e1e1e1e1e1e1");
        private static readonly Guid PhoneCallId = new Guid("e2e2e2e2-e2e2-e2e2-e2e2-e2e2e2e2e2e2");

        private static void AssertCrmError(string expectedCode, Action action)
        {
            var ex = Assert.Throws<InvalidPluginExecutionException>(action);
            Assert.StartsWith("[" + expectedCode + "] ", ex.Message);
            Assert.Contains("(CorrelationId: " + CorrelationId.ToString("D") + ")", ex.Message);
        }

        private static FakeOrganizationService CreateCallerService(
            AccessRights defaultRights = AccessRights.ReadAccess | AccessRights.AppendToAccess,
            HashSet<Guid> deniedTargetIds = null,
            HashSet<string> userRoles = null,
            Func<string, Guid, ColumnSet, Entity> onRetrieve = null)
        {
            return new FakeOrganizationService
            {
                OnRetrieve = onRetrieve,
                OnExecute = req =>
                {
                    var accessReq = req as RetrievePrincipalAccessRequest;
                    if (accessReq != null)
                    {
                        var rights = (deniedTargetIds != null && deniedTargetIds.Contains(accessReq.Target.Id))
                            ? AccessRights.None
                            : defaultRights;
                        var resp = new RetrievePrincipalAccessResponse();
                        resp.Results["AccessRights"] = rights;
                        return resp;
                    }

                    return new OrganizationResponse();
                },
                OnRetrieveMultiple = q =>
                {
                    var qe = q as QueryExpression;
                    if (qe != null && qe.EntityName == CrmSchema.Entities.Role && userRoles != null)
                    {
                        foreach (var cond in qe.Criteria.Conditions)
                        {
                            if (cond.AttributeName == CrmSchema.Platform.RoleName
                                && cond.Values.Count > 0
                                && userRoles.Contains(cond.Values[0] as string))
                            {
                                return new EntityCollection(new[] { new Entity(CrmSchema.Entities.Role, Guid.NewGuid()) });
                            }
                        }
                    }

                    return new EntityCollection();
                }
            };
        }

        private static FakePluginExecutionContext CreateContext(
            string entityName,
            string messageName,
            ParameterCollection inputs,
            Entity preImage = null)
        {
            var ctx = new FakePluginExecutionContext
            {
                PrimaryEntityName = entityName,
                MessageName = messageName,
                Stage = 20,
                Mode = 0,
                IsInTransaction = true,
                CorrelationId = CorrelationId,
                InitiatingUserId = CallerUserId,
                UserId = CallerUserId,
                InputParameters = inputs ?? new ParameterCollection()
            };

            if (preImage != null)
            {
                ctx.PreEntityImages["PreImage"] = preImage;
            }

            return ctx;
        }

        private static void RunPlugin(IPluginExecutionContext context, IOrganizationService callerService)
        {
            var factory = new FakeOrganizationServiceFactory(uid => uid == CallerUserId ? callerService : null);
            new ActivityHistoryGuardPlugin().Execute(new FakeServiceProvider(context, factory));
        }

        private static EntityCollection BuildPartyList(params Entity[] parties)
        {
            return new EntityCollection(parties);
        }

        private static Entity BuildResolvedParty(string entityName, Guid id)
        {
            var party = new Entity(CrmSchema.Entities.ActivityParty);
            party[CrmSchema.ActivityParty.PartyId] = new EntityReference(entityName, id);
            return party;
        }

        private static Entity BuildUnresolvedParty(string addressUsed)
        {
            var party = new Entity(CrmSchema.Entities.ActivityParty);
            party[CrmSchema.ActivityParty.AddressUsed] = addressUsed;
            return party;
        }

        [Fact]
        public void Task_CreateAndOpenUpdate_AllowsClosedWonOpportunityValidatesRegardingDatesAndCompletion()
        {
            var start = new DateTime(2026, 11, 24, 9, 0, 0, DateTimeKind.Utc);
            var end = new DateTime(2026, 11, 24, 17, 0, 0, DateTimeKind.Utc);
            var deniedIds = new HashSet<Guid> { UnauthorizedContactId };
            var caller = CreateCallerService(deniedTargetIds: deniedIds);

            // 1. Valid Tasks on all 4 supported Regarding entities (including Closed Won Opportunity — zero writes to Opportunity!)
            foreach (var regarding in new[]
            {
                new EntityReference(CrmSchema.Entities.Lead, LeadId),
                new EntityReference(CrmSchema.Entities.Opportunity, ClosedWonOppId),
                new EntityReference(CrmSchema.Entities.Account, AccountId),
                new EntityReference(CrmSchema.Entities.Contact, ContactId)
            })
            {
                var task = new Entity(CrmSchema.Entities.Task, TaskId);
                task[CrmSchema.Task.Subject] = "Follow up with customer";
                task[CrmSchema.Task.ScheduledStart] = start;
                task[CrmSchema.Task.ScheduledEnd] = end;
                task[CrmSchema.Task.RegardingObjectId] = regarding;

                RunPlugin(CreateContext(CrmSchema.Entities.Task, "Create", new ParameterCollection { { "Target", task } }), caller);
            }
            // Verify zero Update/Create/Delete requests were ever executed against parent Opportunity
            Assert.All(caller.ExecutedRequests, r => Assert.IsType<RetrievePrincipalAccessRequest>(r));

            // 2. Missing, unsupported, or unauthorized Regarding -> CRM016
            var noRegarding = new Entity(CrmSchema.Entities.Task, TaskId);
            noRegarding[CrmSchema.Task.Subject] = "No regarding";
            noRegarding[CrmSchema.Task.ScheduledEnd] = end;
            AssertCrmError(CrmError.CRM016, () => RunPlugin(CreateContext(CrmSchema.Entities.Task, "Create", new ParameterCollection { { "Target", noRegarding } }), caller));

            var badRegarding = new Entity(CrmSchema.Entities.Task, TaskId);
            badRegarding[CrmSchema.Task.Subject] = "Product regarding";
            badRegarding[CrmSchema.Task.ScheduledEnd] = end;
            badRegarding[CrmSchema.Task.RegardingObjectId] = new EntityReference(CrmSchema.Entities.Product, Guid.NewGuid());
            AssertCrmError(CrmError.CRM016, () => RunPlugin(CreateContext(CrmSchema.Entities.Task, "Create", new ParameterCollection { { "Target", badRegarding } }), caller));

            var unauthRegarding = new Entity(CrmSchema.Entities.Task, TaskId);
            unauthRegarding[CrmSchema.Task.Subject] = "Cross-BU contact";
            unauthRegarding[CrmSchema.Task.ScheduledEnd] = end;
            unauthRegarding[CrmSchema.Task.RegardingObjectId] = new EntityReference(CrmSchema.Entities.Contact, UnauthorizedContactId);
            AssertCrmError(CrmError.CRM016, () => RunPlugin(CreateContext(CrmSchema.Entities.Task, "Create", new ParameterCollection { { "Target", unauthRegarding } }), caller));

            // 3. Missing subject / scheduledend or start > end -> CRM016
            var badDates = new Entity(CrmSchema.Entities.Task, TaskId);
            badDates[CrmSchema.Task.Subject] = "Inverted schedule";
            badDates[CrmSchema.Task.ScheduledStart] = end.AddHours(1);
            badDates[CrmSchema.Task.ScheduledEnd] = end;
            badDates[CrmSchema.Task.RegardingObjectId] = new EntityReference(CrmSchema.Entities.Lead, LeadId);
            AssertCrmError(CrmError.CRM016, () => RunPlugin(CreateContext(CrmSchema.Entities.Task, "Create", new ParameterCollection { { "Target", badDates } }), caller));

            var blankSubject = new Entity(CrmSchema.Entities.Task, TaskId);
            blankSubject[CrmSchema.Task.Subject] = "   ";
            blankSubject[CrmSchema.Task.ScheduledEnd] = end;
            blankSubject[CrmSchema.Task.RegardingObjectId] = new EntityReference(CrmSchema.Entities.Lead, LeadId);
            AssertCrmError(CrmError.CRM016, () => RunPlugin(CreateContext(CrmSchema.Entities.Task, "Create", new ParameterCollection { { "Target", blankSubject } }), caller));

            // 4. Valid Task completions (explicit status=5, omitted status, status=-1) stamp actualend; mismatched status fails CRM016
            var existingOpenTask = new Entity(CrmSchema.Entities.Task, TaskId);
            existingOpenTask[CrmSchema.Task.StateCode] = new OptionSetValue(CrmSchema.Task.State.Open);
            existingOpenTask[CrmSchema.Task.StatusCode] = new OptionSetValue(CrmSchema.Task.Status.NotStarted);
            existingOpenTask[CrmSchema.Task.Subject] = "Prepare deck";
            existingOpenTask[CrmSchema.Task.ScheduledEnd] = end;
            existingOpenTask[CrmSchema.Task.RegardingObjectId] = new EntityReference(CrmSchema.Entities.Opportunity, ClosedWonOppId);
            existingOpenTask[CrmSchema.Task.OwnerId] = new EntityReference(CrmSchema.Entities.SystemUser, CallerUserId);

            foreach (var statusVal in new int?[] { CrmSchema.Task.Status.Completed, null, -1 })
            {
                var completePatch = new Entity(CrmSchema.Entities.Task, TaskId);
                completePatch[CrmSchema.Task.StateCode] = new OptionSetValue(CrmSchema.Task.State.Completed);
                if (statusVal.HasValue)
                {
                    completePatch[CrmSchema.Task.StatusCode] = new OptionSetValue(statusVal.Value);
                }

                RunPlugin(CreateContext(CrmSchema.Entities.Task, "Update", new ParameterCollection { { "Target", completePatch } }, existingOpenTask), caller);
                Assert.True(completePatch.Contains(CrmSchema.Task.ActualEnd));
                Assert.NotNull(completePatch.GetAttributeValue<DateTime?>(CrmSchema.Task.ActualEnd));
            }

            var mismatchedStatus = new Entity(CrmSchema.Entities.Task, TaskId);
            mismatchedStatus[CrmSchema.Task.StateCode] = new OptionSetValue(CrmSchema.Task.State.Completed);
            mismatchedStatus[CrmSchema.Task.StatusCode] = new OptionSetValue(CrmSchema.Task.Status.NotStarted);
            AssertCrmError(CrmError.CRM016, () => RunPlugin(
                CreateContext(CrmSchema.Entities.Task, "Update", new ParameterCollection { { "Target", mismatchedStatus } }, existingOpenTask),
                caller));

            // Rebinding regardingobjectid while closing -> CRM015
            var rebindOnClose = new Entity(CrmSchema.Entities.Task, TaskId);
            rebindOnClose[CrmSchema.Task.StateCode] = new OptionSetValue(CrmSchema.Task.State.Completed);
            rebindOnClose[CrmSchema.Task.StatusCode] = new OptionSetValue(CrmSchema.Task.Status.Completed);
            rebindOnClose[CrmSchema.Task.RegardingObjectId] = new EntityReference(CrmSchema.Entities.Lead, LeadId);
            AssertCrmError(CrmError.CRM015, () => RunPlugin(
                CreateContext(CrmSchema.Entities.Task, "Update", new ParameterCollection { { "Target", rebindOnClose } }, existingOpenTask),
                caller));
        }

        [Fact]
        public void Task_ImmutabilityOwnershipAndAutomationKey_EnforceCrm015AndCrm018()
        {
            var end = new DateTime(2026, 11, 25, 17, 0, 0, DateTimeKind.Utc);
            var repCaller = CreateCallerService(userRoles: new HashSet<string> { CrmSchema.Roles.SalesRepresentative });
            var workerCaller = CreateCallerService(userRoles: new HashSet<string>());

            var openTask = new Entity(CrmSchema.Entities.Task, TaskId);
            openTask[CrmSchema.Task.StateCode] = new OptionSetValue(CrmSchema.Task.State.Open);
            openTask[CrmSchema.Task.StatusCode] = new OptionSetValue(CrmSchema.Task.Status.NotStarted);
            openTask[CrmSchema.Task.Subject] = "Initial onboarding task";
            openTask[CrmSchema.Task.ScheduledEnd] = end;
            openTask[CrmSchema.Task.RegardingObjectId] = new EntityReference(CrmSchema.Entities.Opportunity, ClosedWonOppId);
            openTask[CrmSchema.Task.OwnerId] = new EntityReference(CrmSchema.Entities.SystemUser, CallerUserId);
            openTask[CrmSchema.Task.AutomationKey] = "postqual:" + ClosedWonOppId.ToString("D");

            var completedTask = new Entity(CrmSchema.Entities.Task, TaskId);
            completedTask[CrmSchema.Task.StateCode] = new OptionSetValue(CrmSchema.Task.State.Completed);
            completedTask[CrmSchema.Task.StatusCode] = new OptionSetValue(CrmSchema.Task.Status.Completed);

            var canceledTask = new Entity(CrmSchema.Entities.Task, TaskId);
            canceledTask[CrmSchema.Task.StateCode] = new OptionSetValue(CrmSchema.Task.State.Canceled);
            canceledTask[CrmSchema.Task.StatusCode] = new OptionSetValue(CrmSchema.Task.Status.Canceled);

            // 1. Editing, reopening (Update & SetState), reassigning (Assign), or deleting Completed/Canceled Task -> CRM015
            foreach (var terminalTask in new[] { completedTask, canceledTask })
            {
                var editPatch = new Entity(CrmSchema.Entities.Task, TaskId);
                editPatch[CrmSchema.Task.Subject] = "Tampered history";
                AssertCrmError(CrmError.CRM015, () => RunPlugin(
                    CreateContext(CrmSchema.Entities.Task, "Update", new ParameterCollection { { "Target", editPatch } }, terminalTask),
                    repCaller));

                var reopenSetState = CreateContext(
                    CrmSchema.Entities.Task,
                    "SetState",
                    new ParameterCollection
                    {
                        { "EntityMoniker", new EntityReference(CrmSchema.Entities.Task, TaskId) },
                        { "State", new OptionSetValue(CrmSchema.Task.State.Open) },
                        { "Status", new OptionSetValue(CrmSchema.Task.Status.NotStarted) }
                    },
                    terminalTask);
                AssertCrmError(CrmError.CRM015, () => RunPlugin(reopenSetState, repCaller));

                var assignTerminal = CreateContext(
                    CrmSchema.Entities.Task,
                    "Assign",
                    new ParameterCollection { { "Target", new EntityReference(CrmSchema.Entities.Task, TaskId) } },
                    terminalTask);
                AssertCrmError(CrmError.CRM015, () => RunPlugin(assignTerminal, repCaller));

                var deleteTerminal = CreateContext(
                    CrmSchema.Entities.Task,
                    "Delete",
                    new ParameterCollection { { "Target", new EntityReference(CrmSchema.Entities.Task, TaskId) } },
                    terminalTask);
                AssertCrmError(CrmError.CRM015, () => RunPlugin(deleteTerminal, repCaller));
            }

            // 2. Assign or changed ownerid PATCH on Open Task -> CRM018; no-op same ownerid -> PASSES
            AssertCrmError(CrmError.CRM018, () => RunPlugin(
                CreateContext(CrmSchema.Entities.Task, "Assign", new ParameterCollection { { "Target", new EntityReference(CrmSchema.Entities.Task, TaskId) } }, openTask),
                repCaller));

            var ownerPatch = new Entity(CrmSchema.Entities.Task, TaskId);
            ownerPatch[CrmSchema.Task.OwnerId] = new EntityReference(CrmSchema.Entities.SystemUser, OtherUserId);
            AssertCrmError(CrmError.CRM018, () => RunPlugin(
                CreateContext(CrmSchema.Entities.Task, "Update", new ParameterCollection { { "Target", ownerPatch } }, openTask),
                repCaller));

            var sameOwnerPatch = new Entity(CrmSchema.Entities.Task, TaskId);
            sameOwnerPatch[CrmSchema.Task.OwnerId] = new EntityReference(CrmSchema.Entities.SystemUser, CallerUserId);
            RunPlugin(CreateContext(CrmSchema.Entities.Task, "Update", new ParameterCollection { { "Target", sameOwnerPatch } }, openTask), repCaller);

            // 3. AutomationKey rules on Create (fail-closed)
            var postQualTask = new Entity(CrmSchema.Entities.Task, Guid.NewGuid());
            postQualTask[CrmSchema.Task.Subject] = "Post-qual follow-up";
            postQualTask[CrmSchema.Task.ScheduledEnd] = end;
            postQualTask[CrmSchema.Task.RegardingObjectId] = new EntityReference(CrmSchema.Entities.Opportunity, ClosedWonOppId);
            postQualTask[CrmSchema.Task.AutomationKey] = "postqual:" + ClosedWonOppId.ToString("D");

            // Sales Rep cannot set postqual:{guid} -> CRM015
            AssertCrmError(CrmError.CRM015, () => RunPlugin(
                CreateContext(CrmSchema.Entities.Task, "Create", new ParameterCollection { { "Target", postQualTask } }),
                repCaller));

            // Role-lookup fault -> fail closed with CRM015
            var faultingRoleCaller = new FakeOrganizationService
            {
                OnExecute = req =>
                {
                    var resp = new RetrievePrincipalAccessResponse();
                    resp.Results["AccessRights"] = AccessRights.ReadAccess | AccessRights.AppendToAccess;
                    return resp;
                },
                OnRetrieveMultiple = q => throw new FaultException<OrganizationServiceFault>(
                    new OrganizationServiceFault { ErrorCode = -2147220960, Message = "Missing prvReadRole" },
                    new FaultReason("Missing prvReadRole"))
            };
            AssertCrmError(CrmError.CRM015, () => RunPlugin(
                CreateContext(CrmSchema.Entities.Task, "Create", new ParameterCollection { { "Target", postQualTask } }),
                faultingRoleCaller));

            // Non-Rep/Non-Manager (Worker/Admin automation) setting valid postqual:{guid} -> PASSES
            RunPlugin(CreateContext(CrmSchema.Entities.Task, "Create", new ParameterCollection { { "Target", postQualTask } }), workerCaller);

            // Unrecognized key prefix -> CRM015
            var arbitraryKeyTask = new Entity(CrmSchema.Entities.Task, Guid.NewGuid());
            arbitraryKeyTask[CrmSchema.Task.Subject] = "Bad key";
            arbitraryKeyTask[CrmSchema.Task.ScheduledEnd] = end;
            arbitraryKeyTask[CrmSchema.Task.RegardingObjectId] = new EntityReference(CrmSchema.Entities.Lead, LeadId);
            arbitraryKeyTask[CrmSchema.Task.AutomationKey] = "custom:forged-key";
            AssertCrmError(CrmError.CRM015, () => RunPlugin(
                CreateContext(CrmSchema.Entities.Task, "Create", new ParameterCollection { { "Target", arbitraryKeyTask } }),
                workerCaller));

            // 4. Modifying, clearing, or deleting a keyed Task -> CRM015
            var clearKeyPatch = new Entity(CrmSchema.Entities.Task, TaskId);
            clearKeyPatch[CrmSchema.Task.AutomationKey] = null;
            AssertCrmError(CrmError.CRM015, () => RunPlugin(
                CreateContext(CrmSchema.Entities.Task, "Update", new ParameterCollection { { "Target", clearKeyPatch } }, openTask),
                workerCaller));

            AssertCrmError(CrmError.CRM015, () => RunPlugin(
                CreateContext(CrmSchema.Entities.Task, "Delete", new ParameterCollection { { "Target", new EntityReference(CrmSchema.Entities.Task, TaskId) } }, openTask),
                workerCaller));
        }

        [Fact]
        public void PhoneCall_PartiesTransitionsAndImmutability_EnforceAllRules()
        {
            var end = new DateTime(2026, 11, 25, 16, 0, 0, DateTimeKind.Utc);
            var deniedIds = new HashSet<Guid> { UnauthorizedContactId };
            var caller = CreateCallerService(deniedTargetIds: deniedIds);

            // 1. Valid PhoneCall with systemuser From and resolved contact To defaults directioncode = true and PASSES
            var resolvedCall = new Entity(CrmSchema.Entities.PhoneCall, PhoneCallId);
            resolvedCall[CrmSchema.PhoneCall.Subject] = "Discovery call";
            resolvedCall[CrmSchema.PhoneCall.ScheduledEnd] = end;
            resolvedCall[CrmSchema.PhoneCall.RegardingObjectId] = new EntityReference(CrmSchema.Entities.Opportunity, ClosedWonOppId);
            resolvedCall[CrmSchema.PhoneCall.From] = BuildPartyList(BuildResolvedParty(CrmSchema.Entities.SystemUser, CallerUserId));
            resolvedCall[CrmSchema.PhoneCall.To] = BuildPartyList(BuildResolvedParty(CrmSchema.Entities.Contact, ContactId));
            RunPlugin(CreateContext(CrmSchema.Entities.PhoneCall, "Create", new ParameterCollection { { "Target", resolvedCall } }), caller);
            Assert.True(resolvedCall.GetAttributeValue<bool>(CrmSchema.PhoneCall.DirectionCode));

            // 2. Valid PhoneCall regarding vn_lead with unresolved addressused -> PASSES
            var unresolvedLeadCall = new Entity(CrmSchema.Entities.PhoneCall, Guid.NewGuid());
            unresolvedLeadCall[CrmSchema.PhoneCall.Subject] = "Initial prospect call";
            unresolvedLeadCall[CrmSchema.PhoneCall.ScheduledEnd] = end;
            unresolvedLeadCall[CrmSchema.PhoneCall.RegardingObjectId] = new EntityReference(CrmSchema.Entities.Lead, LeadId);
            unresolvedLeadCall[CrmSchema.PhoneCall.From] = BuildPartyList(BuildResolvedParty(CrmSchema.Entities.SystemUser, CallerUserId));
            unresolvedLeadCall[CrmSchema.PhoneCall.To] = BuildPartyList(BuildUnresolvedParty("+1-555-0199"));
            RunPlugin(CreateContext(CrmSchema.Entities.PhoneCall, "Create", new ParameterCollection { { "Target", unresolvedLeadCall } }), caller);

            // 3. PhoneCall using vn_lead or vn_opportunity as partyid -> CRM016
            var leadPartyCall = new Entity(CrmSchema.Entities.PhoneCall, Guid.NewGuid());
            leadPartyCall[CrmSchema.PhoneCall.Subject] = "Invalid lead party";
            leadPartyCall[CrmSchema.PhoneCall.ScheduledEnd] = end;
            leadPartyCall[CrmSchema.PhoneCall.RegardingObjectId] = new EntityReference(CrmSchema.Entities.Lead, LeadId);
            leadPartyCall[CrmSchema.PhoneCall.From] = BuildPartyList(BuildResolvedParty(CrmSchema.Entities.SystemUser, CallerUserId));
            leadPartyCall[CrmSchema.PhoneCall.To] = BuildPartyList(BuildResolvedParty(CrmSchema.Entities.Lead, LeadId));
            AssertCrmError(CrmError.CRM016, () => RunPlugin(CreateContext(CrmSchema.Entities.PhoneCall, "Create", new ParameterCollection { { "Target", leadPartyCall } }), caller));

            var oppPartyCall = new Entity(CrmSchema.Entities.PhoneCall, Guid.NewGuid());
            oppPartyCall[CrmSchema.PhoneCall.Subject] = "Invalid opp party";
            oppPartyCall[CrmSchema.PhoneCall.ScheduledEnd] = end;
            oppPartyCall[CrmSchema.PhoneCall.RegardingObjectId] = new EntityReference(CrmSchema.Entities.Opportunity, ClosedWonOppId);
            oppPartyCall[CrmSchema.PhoneCall.From] = BuildPartyList(BuildResolvedParty(CrmSchema.Entities.SystemUser, CallerUserId));
            oppPartyCall[CrmSchema.PhoneCall.To] = BuildPartyList(BuildResolvedParty(CrmSchema.Entities.Opportunity, ClosedWonOppId));
            AssertCrmError(CrmError.CRM016, () => RunPlugin(CreateContext(CrmSchema.Entities.PhoneCall, "Create", new ParameterCollection { { "Target", oppPartyCall } }), caller));

            // 4. Empty from/to, null directioncode, or unauthorized Contact party -> CRM016
            var emptyToCall = new Entity(CrmSchema.Entities.PhoneCall, Guid.NewGuid());
            emptyToCall[CrmSchema.PhoneCall.Subject] = "Missing To";
            emptyToCall[CrmSchema.PhoneCall.ScheduledEnd] = end;
            emptyToCall[CrmSchema.PhoneCall.RegardingObjectId] = new EntityReference(CrmSchema.Entities.Account, AccountId);
            emptyToCall[CrmSchema.PhoneCall.From] = BuildPartyList(BuildResolvedParty(CrmSchema.Entities.SystemUser, CallerUserId));
            emptyToCall[CrmSchema.PhoneCall.To] = BuildPartyList();
            AssertCrmError(CrmError.CRM016, () => RunPlugin(CreateContext(CrmSchema.Entities.PhoneCall, "Create", new ParameterCollection { { "Target", emptyToCall } }), caller));

            // Explicit null directioncode on Create defaults to true (Outgoing)
            var nullDirectionCall = new Entity(CrmSchema.Entities.PhoneCall, Guid.NewGuid());
            nullDirectionCall[CrmSchema.PhoneCall.Subject] = "Default direction";
            nullDirectionCall[CrmSchema.PhoneCall.ScheduledEnd] = end;
            nullDirectionCall[CrmSchema.PhoneCall.DirectionCode] = null;
            nullDirectionCall[CrmSchema.PhoneCall.RegardingObjectId] = new EntityReference(CrmSchema.Entities.Account, AccountId);
            nullDirectionCall[CrmSchema.PhoneCall.From] = BuildPartyList(BuildResolvedParty(CrmSchema.Entities.SystemUser, CallerUserId));
            nullDirectionCall[CrmSchema.PhoneCall.To] = BuildPartyList(BuildResolvedParty(CrmSchema.Entities.Contact, ContactId));
            RunPlugin(CreateContext(CrmSchema.Entities.PhoneCall, "Create", new ParameterCollection { { "Target", nullDirectionCall } }), caller);
            Assert.True(nullDirectionCall.GetAttributeValue<bool>(CrmSchema.PhoneCall.DirectionCode));

            var unauthPartyCall = new Entity(CrmSchema.Entities.PhoneCall, Guid.NewGuid());
            unauthPartyCall[CrmSchema.PhoneCall.Subject] = "Unauthorized party";
            unauthPartyCall[CrmSchema.PhoneCall.ScheduledEnd] = end;
            unauthPartyCall[CrmSchema.PhoneCall.RegardingObjectId] = new EntityReference(CrmSchema.Entities.Account, AccountId);
            unauthPartyCall[CrmSchema.PhoneCall.From] = BuildPartyList(BuildResolvedParty(CrmSchema.Entities.SystemUser, CallerUserId));
            unauthPartyCall[CrmSchema.PhoneCall.To] = BuildPartyList(BuildResolvedParty(CrmSchema.Entities.Contact, UnauthorizedContactId));
            AssertCrmError(CrmError.CRM016, () => RunPlugin(CreateContext(CrmSchema.Entities.PhoneCall, "Create", new ParameterCollection { { "Target", unauthPartyCall } }), caller));

            // 5. Valid completion (Made=2, Received=4) & SetState with Status=-1 pass; invalid status (state=1, status=3) fails CRM016
            var openCall = new Entity(CrmSchema.Entities.PhoneCall, PhoneCallId);
            openCall[CrmSchema.PhoneCall.StateCode] = new OptionSetValue(CrmSchema.PhoneCall.State.Open);
            openCall[CrmSchema.PhoneCall.StatusCode] = new OptionSetValue(CrmSchema.PhoneCall.Status.Open);
            openCall[CrmSchema.PhoneCall.Subject] = "Discovery call";
            openCall[CrmSchema.PhoneCall.ScheduledEnd] = end;
            openCall[CrmSchema.PhoneCall.RegardingObjectId] = new EntityReference(CrmSchema.Entities.Opportunity, ClosedWonOppId);
            openCall[CrmSchema.PhoneCall.OwnerId] = new EntityReference(CrmSchema.Entities.SystemUser, CallerUserId);

            foreach (var validCompletedStatus in new[] { CrmSchema.PhoneCall.Status.Made, CrmSchema.PhoneCall.Status.Received })
            {
                var completeCall = new Entity(CrmSchema.Entities.PhoneCall, PhoneCallId);
                completeCall[CrmSchema.PhoneCall.StateCode] = new OptionSetValue(CrmSchema.PhoneCall.State.Completed);
                completeCall[CrmSchema.PhoneCall.StatusCode] = new OptionSetValue(validCompletedStatus);
                RunPlugin(CreateContext(CrmSchema.Entities.PhoneCall, "Update", new ParameterCollection { { "Target", completeCall } }, openCall), caller);
                Assert.True(completeCall.Contains(CrmSchema.PhoneCall.ActualEnd));
            }

            var setStateDefault = CreateContext(
                CrmSchema.Entities.PhoneCall,
                "SetState",
                new ParameterCollection
                {
                    { "EntityMoniker", new EntityReference(CrmSchema.Entities.PhoneCall, PhoneCallId) },
                    { "State", new OptionSetValue(CrmSchema.PhoneCall.State.Completed) },
                    { "Status", new OptionSetValue(-1) }
                },
                openCall);
            RunPlugin(setStateDefault, caller);

            var badStatusCall = new Entity(CrmSchema.Entities.PhoneCall, PhoneCallId);
            badStatusCall[CrmSchema.PhoneCall.StateCode] = new OptionSetValue(CrmSchema.PhoneCall.State.Completed);
            badStatusCall[CrmSchema.PhoneCall.StatusCode] = new OptionSetValue(CrmSchema.PhoneCall.Status.Canceled);
            AssertCrmError(CrmError.CRM016, () => RunPlugin(
                CreateContext(CrmSchema.Entities.PhoneCall, "Update", new ParameterCollection { { "Target", badStatusCall } }, openCall),
                caller));

            // 6. Editing, reopening, or deleting Completed/Canceled PhoneCall -> CRM015
            var completedCall = new Entity(CrmSchema.Entities.PhoneCall, PhoneCallId);
            completedCall[CrmSchema.PhoneCall.StateCode] = new OptionSetValue(CrmSchema.PhoneCall.State.Completed);
            completedCall[CrmSchema.PhoneCall.StatusCode] = new OptionSetValue(CrmSchema.PhoneCall.Status.Made);

            var editCompleted = new Entity(CrmSchema.Entities.PhoneCall, PhoneCallId);
            editCompleted[CrmSchema.PhoneCall.Subject] = "Altered call notes";
            AssertCrmError(CrmError.CRM015, () => RunPlugin(
                CreateContext(CrmSchema.Entities.PhoneCall, "Update", new ParameterCollection { { "Target", editCompleted } }, completedCall),
                caller));

            var reopenCompleted = CreateContext(
                CrmSchema.Entities.PhoneCall,
                "SetState",
                new ParameterCollection
                {
                    { "EntityMoniker", new EntityReference(CrmSchema.Entities.PhoneCall, PhoneCallId) },
                    { "State", new OptionSetValue(CrmSchema.PhoneCall.State.Open) },
                    { "Status", new OptionSetValue(CrmSchema.PhoneCall.Status.Open) }
                },
                completedCall);
            AssertCrmError(CrmError.CRM015, () => RunPlugin(reopenCompleted, caller));

            var deleteCompleted = CreateContext(
                CrmSchema.Entities.PhoneCall,
                "Delete",
                new ParameterCollection { { "Target", new EntityReference(CrmSchema.Entities.PhoneCall, PhoneCallId) } },
                completedCall);
            AssertCrmError(CrmError.CRM015, () => RunPlugin(deleteCompleted, caller));
        }
    }
}
