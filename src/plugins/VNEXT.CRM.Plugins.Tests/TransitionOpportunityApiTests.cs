using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using VNEXT.CRM.Plugins.Apis;
using VNEXT.CRM.Plugins.Core;
using Xunit;

namespace VNEXT.CRM.Plugins.Tests
{
    public class TransitionOpportunityApiTests
    {
        private static readonly Guid CorrelationId = new Guid("e1e1e1e1-1111-1111-1111-111111111111");
        private static readonly Guid BaseCurrencyId = new Guid("11111111-1111-1111-1111-111111111111");
        private static readonly Guid RepUserId = new Guid("22222222-2222-2222-2222-222222222222");
        private static readonly Guid ManagerUserId = new Guid("33333333-3333-3333-3333-333333333333");
        private static readonly Guid WriterUserId = new Guid("44444444-4444-4444-4444-444444444444");
        private static readonly Guid SalesABuId = new Guid("55555555-5555-5555-5555-555555555555");
        private static readonly Guid AccountId = new Guid("66666666-6666-6666-6666-666666666666");
        private static readonly Guid OtherAccountId = new Guid("67676767-6767-6767-6767-676767676767");
        private static readonly Guid ContactId = new Guid("77777777-7777-7777-7777-777777777777");
        private static readonly Guid OppId = new Guid("88888888-8888-8888-8888-888888888888");
        private static readonly Guid BpfId = new Guid("99999999-9999-9999-9999-999999999999");
        private static readonly Guid ApprovalId = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        private static void AssertCrmError(string expectedCode, Action action)
        {
            var ex = Assert.Throws<InvalidPluginExecutionException>(action);
            Assert.StartsWith("[" + expectedCode + "] ", ex.Message);
            Assert.Contains("(CorrelationId: " + CorrelationId.ToString("D") + ")", ex.Message);
        }

        private static Entity BuildOpenOpportunity(
            int stage,
            decimal total = 0m,
            decimal maxDiscount = 0m,
            Guid? currentApprovalId = null,
            int commandSequence = 3,
            string rowVersion = "2001")
        {
            var opp = new Entity(CrmSchema.Entities.Opportunity, OppId)
            {
                RowVersion = rowVersion
            };
            opp[CrmSchema.Opportunity.StateCode] = new OptionSetValue(CrmSchema.Opportunity.State.Active);
            opp[CrmSchema.Opportunity.StatusCode] = new OptionSetValue(CrmSchema.Opportunity.Status.Open);
            opp[CrmSchema.Opportunity.Stage] = new OptionSetValue(stage);
            opp[CrmSchema.Opportunity.Probability] = 10;
            opp[CrmSchema.Opportunity.CommandSequence] = commandSequence;
            opp[CrmSchema.Opportunity.Account] = new EntityReference(CrmSchema.Entities.Account, AccountId);
            opp[CrmSchema.Opportunity.PrimaryContact] = new EntityReference(CrmSchema.Entities.Contact, ContactId);
            opp[CrmSchema.Opportunity.Need] = "Unified enterprise pipeline governance.";
            opp[CrmSchema.Opportunity.DecisionMaker] = "VP of Sales";
            opp[CrmSchema.Opportunity.ProposalSummary] = "3-year enterprise subscription with onboarding.";
            opp[CrmSchema.Opportunity.NextStep] = "Obtain signed commercial agreement.";
            opp[CrmSchema.Opportunity.ExpectedCloseDate] = DateTime.UtcNow.Date.AddDays(15);
            opp[CrmSchema.Opportunity.NextFollowUpDate] = DateTime.UtcNow.Date.AddDays(2);
            opp[CrmSchema.Opportunity.Total] = new Money(total);
            opp[CrmSchema.Opportunity.MaxDiscount] = maxDiscount;
            opp[CrmSchema.Opportunity.OwnerId] = new EntityReference(CrmSchema.Entities.SystemUser, RepUserId);
            opp[CrmSchema.Common.CreatedOn] = DateTime.UtcNow.Date.AddDays(-5).AddHours(8);

            if (currentApprovalId.HasValue)
            {
                opp[CrmSchema.Opportunity.CurrentApproval] = new EntityReference(CrmSchema.Entities.ApprovalRequest, currentApprovalId.Value);
            }

            return opp;
        }

        private static Entity BuildActiveLine(decimal netAmount, decimal discountPercent)
        {
            var line = new Entity(CrmSchema.Entities.OpportunityLine, Guid.NewGuid());
            line[CrmSchema.OpportunityLine.Opportunity] = new EntityReference(CrmSchema.Entities.Opportunity, OppId);
            line[CrmSchema.OpportunityLine.StateCode] = new OptionSetValue(CrmSchema.OpportunityLine.State.Active);
            line[CrmSchema.OpportunityLine.NetAmount] = new Money(netAmount);
            line[CrmSchema.OpportunityLine.DiscountPercent] = discountPercent;
            return line;
        }

        private static Entity BuildApprovalRequest(int resultValue, decimal total, decimal maxDiscount)
        {
            var req = new Entity(CrmSchema.Entities.ApprovalRequest, ApprovalId);
            req[CrmSchema.ApprovalRequest.Opportunity] = new EntityReference(CrmSchema.Entities.Opportunity, OppId);
            req[CrmSchema.ApprovalRequest.Result] = new OptionSetValue(resultValue);
            req[CrmSchema.ApprovalRequest.DealOwner] = new EntityReference(CrmSchema.Entities.SystemUser, RepUserId);
            req[CrmSchema.ApprovalRequest.Approver] = new EntityReference(CrmSchema.Entities.SystemUser, ManagerUserId);
            req[CrmSchema.ApprovalRequest.Responder] = new EntityReference(CrmSchema.Entities.SystemUser, ManagerUserId);
            req[CrmSchema.ApprovalRequest.RespondedOn] = DateTime.UtcNow.AddHours(-1);
            req[CrmSchema.ApprovalRequest.Total] = new Money(total);
            req[CrmSchema.ApprovalRequest.MaxDiscount] = maxDiscount;
            return req;
        }

        private static FakePluginExecutionContext BuildApiContext(
            string command,
            string rowVersion = "W/\"2001\"",
            int? targetStage = null,
            string comments = null,
            DateTime? actualCloseDate = null,
            int? lossReason = null,
            bool isInTransaction = true)
        {
            var inputs = new ParameterCollection
            {
                { CrmSchema.Apis.Params.Target, new EntityReference(CrmSchema.Entities.Opportunity, OppId) },
                { CrmSchema.Apis.Params.Command, command }
            };

            if (rowVersion != null) inputs[CrmSchema.Apis.Params.ExpectedRowVersion] = rowVersion;
            if (targetStage.HasValue) inputs[CrmSchema.Apis.Params.TargetStage] = targetStage.Value;
            if (comments != null) inputs[CrmSchema.Apis.Params.Comments] = comments;
            if (actualCloseDate.HasValue) inputs[CrmSchema.Apis.Params.ActualCloseDate] = actualCloseDate.Value;
            if (lossReason.HasValue) inputs[CrmSchema.Apis.Params.LossReason] = lossReason.Value;

            return new FakePluginExecutionContext
            {
                MessageName = CrmSchema.Apis.TransitionOpportunity,
                PrimaryEntityName = CrmSchema.Entities.Opportunity,
                PrimaryEntityId = OppId,
                Stage = 30,
                Mode = 0,
                IsInTransaction = isInTransaction,
                CorrelationId = CorrelationId,
                InitiatingUserId = RepUserId,
                UserId = RepUserId,
                InputParameters = inputs
            };
        }

        private static ApiFakeOrganizationService BuildCallerService(
            Entity opp,
            IEnumerable<Entity> activeLines = null,
            bool configureWriter = true,
            AccessRights oppRights = AccessRights.ReadAccess | AccessRights.WriteAccess,
            bool accountActive = true,
            bool contactActive = true,
            Guid? contactParentAccountId = null,
            List<string> callLog = null)
        {
            var account = new Entity(CrmSchema.Entities.Account, AccountId);
            account[CrmSchema.Account.StateCode] = new OptionSetValue(accountActive ? CrmSchema.Account.State.Active : CrmSchema.Account.State.Inactive);

            var contact = new Entity(CrmSchema.Entities.Contact, ContactId);
            contact[CrmSchema.Contact.StateCode] = new OptionSetValue(contactActive ? CrmSchema.Contact.State.Active : CrmSchema.Contact.State.Inactive);
            contact[CrmSchema.Contact.ParentCustomerId] = new EntityReference(CrmSchema.Entities.Account, contactParentAccountId ?? AccountId);

            var lineList = activeLines != null ? activeLines.ToList() : new List<Entity>();

            return new ApiFakeOrganizationService
            {
                OnRetrieve = (entityName, id, cols) =>
                {
                    if (entityName == CrmSchema.Entities.Opportunity && id == OppId) return opp;
                    if (entityName == CrmSchema.Entities.Account && id == AccountId) return account;
                    if (entityName == CrmSchema.Entities.Contact && id == ContactId) return contact;
                    return null;
                },
                OnUpdate = entity =>
                {
                    if (callLog != null) callLog.Add("Update:" + entity.LogicalName);
                },
                OnExecute = req =>
                {
                    if (req is UpdateRequest)
                    {
                        if (callLog != null) callLog.Add("Latch:BumpCommandSequence");
                        return new UpdateResponse();
                    }

                    var accessReq = req as RetrievePrincipalAccessRequest;
                    if (accessReq != null)
                    {
                        var resp = new RetrievePrincipalAccessResponse();
                        resp.Results["AccessRights"] = oppRights;
                        return resp;
                    }

                    return new OrganizationResponse();
                },
                OnRetrieveMultiple = q =>
                {
                    var qe = q as QueryExpression;
                    if (qe != null)
                    {
                        if (qe.EntityName == CrmSchema.Entities.EnvironmentVariableDefinition)
                        {
                            if (!configureWriter) return new EntityCollection();
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

                        if (qe.EntityName == CrmSchema.Entities.OpportunityLine)
                        {
                            if (callLog != null) callLog.Add("Query:Lines");
                            return new EntityCollection(lineList);
                        }
                    }

                    return new EntityCollection();
                }
            };
        }

        private static ApiFakeOrganizationService BuildWriterService(Entity approvalRequest = null)
        {
            var bpfInstance = new Entity(CrmSchema.Entities.OpportunitySalesProcess, BpfId);
            bpfInstance[CrmSchema.Bpf.Opportunity] = new EntityReference(CrmSchema.Entities.Opportunity, OppId);

            return new ApiFakeOrganizationService
            {
                OnRetrieve = (entityName, id, cols) =>
                {
                    if (entityName == CrmSchema.Entities.SystemUser)
                    {
                        var u = new Entity(CrmSchema.Entities.SystemUser, id);
                        u[CrmSchema.Platform.IsDisabled] = false;
                        u[CrmSchema.Platform.BusinessUnitId] = new EntityReference(CrmSchema.Entities.BusinessUnit, SalesABuId);
                        return u;
                    }

                    if (entityName == CrmSchema.Entities.ApprovalRequest && approvalRequest != null && id == approvalRequest.Id)
                    {
                        return approvalRequest;
                    }

                    return null;
                },
                OnRetrieveMultiple = q =>
                {
                    var qe = q as QueryExpression;
                    if (qe != null && qe.EntityName == CrmSchema.Entities.OpportunitySalesProcess)
                    {
                        return new EntityCollection(new[] { bpfInstance });
                    }

                    return new EntityCollection();
                }
            };
        }

        private static void ExecuteApi(
            IPluginExecutionContext ctx,
            IOrganizationService callerService,
            IOrganizationService writerService = null)
        {
            var effectiveWriter = writerService ?? BuildWriterService();
            var factory = new FakeOrganizationServiceFactory(uid =>
                uid == RepUserId ? callerService :
                uid == WriterUserId ? effectiveWriter : null);

            new TransitionOpportunityApiPlugin().Execute(new FakeServiceProvider(ctx, factory));
        }

        [Fact]
        public void Advance_QualifyDevelopProposeClose_BumpsLatchFirstMirrorsBpfAndSetsExactOutputs()
        {
            var transitions = new[]
            {
                Tuple.Create(CrmSchema.Opportunity.StageValues.Qualify, (int?)null, CrmSchema.Opportunity.StageValues.Develop, 30),
                Tuple.Create(CrmSchema.Opportunity.StageValues.Develop, (int?)CrmSchema.Opportunity.StageValues.Propose, CrmSchema.Opportunity.StageValues.Propose, 60),
                Tuple.Create(CrmSchema.Opportunity.StageValues.Propose, (int?)CrmSchema.Opportunity.StageValues.Close, CrmSchema.Opportunity.StageValues.Close, 90)
            };

            foreach (var step in transitions)
            {
                var callLog = new List<string>();
                var opp = BuildOpenOpportunity(step.Item1, total: 15000m, maxDiscount: 5m, commandSequence: 3);
                var lines = new[] { BuildActiveLine(15000m, 5m) };
                var caller = BuildCallerService(opp, activeLines: lines, callLog: callLog);
                var writer = BuildWriterService();
                var ctx = BuildApiContext("Advance", targetStage: step.Item2);

                ExecuteApi(ctx, caller, writer);

                // Latch MUST execute FIRST before any Line query or stage Update
                Assert.Equal("Latch:BumpCommandSequence", callLog[0]);
                var latchReq = caller.ExecutedRequests.OfType<UpdateRequest>().Single();
                Assert.Equal(ConcurrencyBehavior.IfRowVersionMatches, latchReq.ConcurrencyBehavior);
                Assert.Equal("2001", latchReq.Target.RowVersion);
                Assert.Equal(4, latchReq.Target.GetAttributeValue<int>(CrmSchema.Opportunity.CommandSequence));

                // Opportunity Update sets ONLY stage, probability, stageenteredon, lastprogresson
                Assert.Single(caller.UpdatedEntities);
                var oppPatch = caller.UpdatedEntities[0];
                Assert.Equal(4, oppPatch.Attributes.Count);
                Assert.Equal(step.Item3, oppPatch.GetAttributeValue<OptionSetValue>(CrmSchema.Opportunity.Stage).Value);
                Assert.Equal(step.Item4, oppPatch.GetAttributeValue<int>(CrmSchema.Opportunity.Probability));
                Assert.True(oppPatch.Contains(CrmSchema.Opportunity.StageEnteredOn));
                Assert.True(oppPatch.Contains(CrmSchema.Opportunity.LastProgressOn));

                // BPF mirrored via writerService
                Assert.Single(writer.UpdatedEntities);
                Assert.Equal(CrmSchema.Entities.OpportunitySalesProcess, writer.UpdatedEntities[0].LogicalName);

                // All 4 OutputParameters exact
                Assert.Equal(step.Item3, (int)ctx.OutputParameters[CrmSchema.Apis.Params.Stage]);
                Assert.Equal(CrmSchema.Opportunity.Status.Open, (int)ctx.OutputParameters[CrmSchema.Apis.Params.StatusReason]);
                Assert.Equal(4, (int)ctx.OutputParameters[CrmSchema.Apis.Params.CommandSequence]);
                Assert.Equal(CorrelationId.ToString("D"), (string)ctx.OutputParameters[CrmSchema.Apis.Params.CorrelationId]);
            }
        }

        [Fact]
        public void Advance_NegativePaths_EnforceCrm006StageOrderAndCrm007Br12Readiness()
        {
            var oppQualify = BuildOpenOpportunity(CrmSchema.Opportunity.StageValues.Qualify);
            var oppDevelop = BuildOpenOpportunity(CrmSchema.Opportunity.StageValues.Develop);
            var oppClose = BuildOpenOpportunity(CrmSchema.Opportunity.StageValues.Close);

            // Invalid commands, skip, back, from-Close -> CRM006
            AssertCrmError(CrmError.CRM006, () => ExecuteApi(BuildApiContext("Back"), BuildCallerService(oppDevelop)));
            AssertCrmError(CrmError.CRM006, () => ExecuteApi(BuildApiContext("Reassign"), BuildCallerService(oppDevelop)));
            AssertCrmError(CrmError.CRM006, () => ExecuteApi(BuildApiContext("ChangeCustomer"), BuildCallerService(oppDevelop)));
            AssertCrmError(CrmError.CRM006, () => ExecuteApi(BuildApiContext("Advance", targetStage: CrmSchema.Opportunity.StageValues.Propose), BuildCallerService(oppQualify)));
            AssertCrmError(CrmError.CRM006, () => ExecuteApi(BuildApiContext("Advance", targetStage: CrmSchema.Opportunity.StageValues.Qualify), BuildCallerService(oppDevelop)));
            AssertCrmError(CrmError.CRM006, () => ExecuteApi(BuildApiContext("Advance"), BuildCallerService(oppClose)));

            // BR12 missing Need / DecisionMaker / inactive Contact / wrong-Account Contact / missing ProposalSummary / zero Lines -> CRM007
            var noNeed = BuildOpenOpportunity(CrmSchema.Opportunity.StageValues.Qualify);
            noNeed[CrmSchema.Opportunity.Need] = " ";
            AssertCrmError(CrmError.CRM007, () => ExecuteApi(BuildApiContext("Advance"), BuildCallerService(noNeed)));

            var noDm = BuildOpenOpportunity(CrmSchema.Opportunity.StageValues.Qualify);
            noDm[CrmSchema.Opportunity.DecisionMaker] = null;
            AssertCrmError(CrmError.CRM007, () => ExecuteApi(BuildApiContext("Advance"), BuildCallerService(noDm)));

            AssertCrmError(CrmError.CRM007, () => ExecuteApi(
                BuildApiContext("Advance"),
                BuildCallerService(oppQualify, contactActive: false)));

            AssertCrmError(CrmError.CRM007, () => ExecuteApi(
                BuildApiContext("Advance"),
                BuildCallerService(oppQualify, contactParentAccountId: OtherAccountId)));

            var noProposal = BuildOpenOpportunity(CrmSchema.Opportunity.StageValues.Develop);
            noProposal[CrmSchema.Opportunity.ProposalSummary] = "  ";
            AssertCrmError(CrmError.CRM007, () => ExecuteApi(
                BuildApiContext("Advance"),
                BuildCallerService(noProposal, activeLines: new[] { BuildActiveLine(5000m, 0m) })));

            AssertCrmError(CrmError.CRM007, () => ExecuteApi(
                BuildApiContext("Advance"),
                BuildCallerService(oppDevelop, activeLines: Array.Empty<Entity>())));
        }

        [Fact]
        public void Won_BelowThresholdPasses_TotalDriftRejectsCrm009_AndApprovalTriggeredEnforcesCrm012()
        {
            var today = DateTime.UtcNow.Date;

            // 1. Non-approval-triggered Won (49,999.99 @ 10.00%) -> PASSES without Request
            var oppLow = BuildOpenOpportunity(CrmSchema.Opportunity.StageValues.Close, total: 49999.99m, maxDiscount: 10.00m);
            var callerLow = BuildCallerService(oppLow, activeLines: new[] { BuildActiveLine(49999.99m, 10.00m) });
            var writerLow = BuildWriterService();
            var ctxLow = BuildApiContext("Won", comments: "Signed purchase order.", actualCloseDate: today);

            ExecuteApi(ctxLow, callerLow, writerLow);

            Assert.Single(callerLow.UpdatedEntities);
            var wonPatch = callerLow.UpdatedEntities[0];
            Assert.Equal(CrmSchema.Opportunity.State.Inactive, wonPatch.GetAttributeValue<OptionSetValue>(CrmSchema.Opportunity.StateCode).Value);
            Assert.Equal(CrmSchema.Opportunity.Status.Won, wonPatch.GetAttributeValue<OptionSetValue>(CrmSchema.Opportunity.StatusCode).Value);
            Assert.Equal(100, wonPatch.GetAttributeValue<int>(CrmSchema.Opportunity.Probability));
            Assert.Equal(49999.99m, wonPatch.GetAttributeValue<Money>(CrmSchema.Opportunity.ClosedAmount).Value);
            Assert.Equal(CrmSchema.Bpf.StatusFinished, writerLow.UpdatedEntities[0].GetAttributeValue<OptionSetValue>(CrmSchema.Bpf.StatusCode).Value);
            Assert.Equal(CrmSchema.Opportunity.Status.Won, (int)ctxLow.OutputParameters[CrmSchema.Apis.Params.StatusReason]);

            // 2. Stored Opportunity total vs live Lines drift -> CRM009
            var oppDrift = BuildOpenOpportunity(CrmSchema.Opportunity.StageValues.Close, total: 10000m, maxDiscount: 0m);
            var callerDrift = BuildCallerService(oppDrift, activeLines: new[] { BuildActiveLine(12000m, 0m) });
            AssertCrmError(CrmError.CRM009, () => ExecuteApi(
                BuildApiContext("Won", comments: "Signed.", actualCloseDate: today),
                callerDrift));

            // 3. Approval-triggered Won (Total = 50,000.00 OR MaxDiscount = 10.01%) WITHOUT valid Approved Request -> CRM012
            var oppHighNoPtr = BuildOpenOpportunity(CrmSchema.Opportunity.StageValues.Close, total: 50000.00m, maxDiscount: 0m, currentApprovalId: null);
            AssertCrmError(CrmError.CRM012, () => ExecuteApi(
                BuildApiContext("Won", comments: "Signed.", actualCloseDate: today),
                BuildCallerService(oppHighNoPtr, activeLines: new[] { BuildActiveLine(50000.00m, 0m) })));

            var oppHighDiscNoPtr = BuildOpenOpportunity(CrmSchema.Opportunity.StageValues.Close, total: 1000.00m, maxDiscount: 10.01m, currentApprovalId: null);
            AssertCrmError(CrmError.CRM012, () => ExecuteApi(
                BuildApiContext("Won", comments: "Signed.", actualCloseDate: today),
                BuildCallerService(oppHighDiscNoPtr, activeLines: new[] { BuildActiveLine(1000.00m, 10.01m) })));

            var oppHighWithPtr = BuildOpenOpportunity(CrmSchema.Opportunity.StageValues.Close, total: 50000.00m, maxDiscount: 5.00m, currentApprovalId: ApprovalId);
            foreach (var badStatus in new[]
            {
                CrmSchema.ApprovalRequest.ResultValues.Pending,
                CrmSchema.ApprovalRequest.ResultValues.Rejected,
                CrmSchema.ApprovalRequest.ResultValues.Superseded
            })
            {
                var badReq = BuildApprovalRequest(badStatus, 50000.00m, 5.00m);
                AssertCrmError(CrmError.CRM012, () => ExecuteApi(
                    BuildApiContext("Won", comments: "Signed.", actualCloseDate: today),
                    BuildCallerService(oppHighWithPtr, activeLines: new[] { BuildActiveLine(50000.00m, 5.00m) }),
                    BuildWriterService(badReq)));
            }

            // Mismatched Approved Request snapshot total -> CRM012
            var staleApprovedReq = BuildApprovalRequest(CrmSchema.ApprovalRequest.ResultValues.Approved, 48000.00m, 5.00m);
            AssertCrmError(CrmError.CRM012, () => ExecuteApi(
                BuildApiContext("Won", comments: "Signed.", actualCloseDate: today),
                BuildCallerService(oppHighWithPtr, activeLines: new[] { BuildActiveLine(50000.00m, 5.00m) }),
                BuildWriterService(staleApprovedReq)));

            // 4. Genuine current Approved Request matching live Lines -> PASSES
            var validApprovedReq = BuildApprovalRequest(CrmSchema.ApprovalRequest.ResultValues.Approved, 50000.00m, 5.00m);
            var callerHighOk = BuildCallerService(oppHighWithPtr, activeLines: new[] { BuildActiveLine(50000.00m, 5.00m) });
            var writerHighOk = BuildWriterService(validApprovedReq);
            var ctxHighOk = BuildApiContext("Won", comments: "Executive approved and signed.", actualCloseDate: today);
            ExecuteApi(ctxHighOk, callerHighOk, writerHighOk);
            Assert.Equal(50000.00m, callerHighOk.UpdatedEntities[0].GetAttributeValue<Money>(CrmSchema.Opportunity.ClosedAmount).Value);
            Assert.Equal(100, callerHighOk.UpdatedEntities[0].GetAttributeValue<int>(CrmSchema.Opportunity.Probability));

            // 5. Won eligibility negatives (non-Close, zero lines, blank comments, date out of bounds) -> CRM014
            var oppPropose = BuildOpenOpportunity(CrmSchema.Opportunity.StageValues.Propose, total: 5000m, maxDiscount: 0m);
            AssertCrmError(CrmError.CRM014, () => ExecuteApi(
                BuildApiContext("Won", comments: "Signed.", actualCloseDate: today),
                BuildCallerService(oppPropose, activeLines: new[] { BuildActiveLine(5000m, 0m) })));

            var oppZeroLines = BuildOpenOpportunity(CrmSchema.Opportunity.StageValues.Close, total: 0m, maxDiscount: 0m);
            AssertCrmError(CrmError.CRM014, () => ExecuteApi(
                BuildApiContext("Won", comments: "Signed.", actualCloseDate: today),
                BuildCallerService(oppZeroLines, activeLines: Array.Empty<Entity>())));

            AssertCrmError(CrmError.CRM014, () => ExecuteApi(
                BuildApiContext("Won", comments: "   ", actualCloseDate: today),
                BuildCallerService(oppLow, activeLines: new[] { BuildActiveLine(49999.99m, 10.00m) })));

            AssertCrmError(CrmError.CRM014, () => ExecuteApi(
                BuildApiContext("Won", comments: "Signed.", actualCloseDate: today.AddDays(-6)),
                BuildCallerService(oppLow, activeLines: new[] { BuildActiveLine(49999.99m, 10.00m) })));

            AssertCrmError(CrmError.CRM014, () => ExecuteApi(
                BuildApiContext("Won", comments: "Signed.", actualCloseDate: today.AddDays(1)),
                BuildCallerService(oppLow, activeLines: new[] { BuildActiveLine(49999.99m, 10.00m) })));
        }

        [Fact]
        public void Lost_FromQualifyWithZeroLines_PreservesTotalAndMirrorsBpfAborted()
        {
            var today = DateTime.UtcNow.Date;
            var oppQualify = BuildOpenOpportunity(CrmSchema.Opportunity.StageValues.Qualify, total: 0m, maxDiscount: 0m);
            var caller = BuildCallerService(oppQualify, activeLines: Array.Empty<Entity>());
            var writer = BuildWriterService();
            var ctx = BuildApiContext(
                "Lost",
                comments: "Customer selected incumbent vendor.",
                actualCloseDate: today,
                lossReason: CrmSchema.Opportunity.LossReasonValues.Competitor);

            ExecuteApi(ctx, caller, writer);

            Assert.Single(caller.UpdatedEntities);
            var lostPatch = caller.UpdatedEntities[0];
            Assert.Equal(CrmSchema.Opportunity.State.Inactive, lostPatch.GetAttributeValue<OptionSetValue>(CrmSchema.Opportunity.StateCode).Value);
            Assert.Equal(CrmSchema.Opportunity.Status.Lost, lostPatch.GetAttributeValue<OptionSetValue>(CrmSchema.Opportunity.StatusCode).Value);
            Assert.Equal(0, lostPatch.GetAttributeValue<int>(CrmSchema.Opportunity.Probability));
            Assert.Equal(0m, lostPatch.GetAttributeValue<Money>(CrmSchema.Opportunity.ClosedAmount).Value);
            Assert.Equal(CrmSchema.Opportunity.LossReasonValues.Competitor, lostPatch.GetAttributeValue<OptionSetValue>(CrmSchema.Opportunity.LossReason).Value);
            Assert.False(lostPatch.Contains(CrmSchema.Opportunity.Total)); // preserves proposed vn_total (BR23)

            Assert.Equal(CrmSchema.Bpf.StatusAborted, writer.UpdatedEntities[0].GetAttributeValue<OptionSetValue>(CrmSchema.Bpf.StatusCode).Value);
            Assert.Equal(CrmSchema.Opportunity.Status.Lost, (int)ctx.OutputParameters[CrmSchema.Apis.Params.StatusReason]);

            // Lost negatives: missing/invalid LossReason (including Dataverse OData default(int)/default(DateTime) injection), blank comments, future date -> CRM014
            AssertCrmError(CrmError.CRM014, () => ExecuteApi(
                BuildApiContext("Lost", comments: "Lost.", actualCloseDate: today, lossReason: null),
                BuildCallerService(oppQualify)));
            AssertCrmError(CrmError.CRM014, () => ExecuteApi(
                BuildApiContext("Lost", comments: "Lost.", actualCloseDate: DateTime.MinValue, lossReason: 0),
                BuildCallerService(oppQualify)));
            AssertCrmError(CrmError.CRM014, () => ExecuteApi(
                BuildApiContext("Lost", comments: "Lost.", actualCloseDate: today, lossReason: 999),
                BuildCallerService(oppQualify)));
            AssertCrmError(CrmError.CRM014, () => ExecuteApi(
                BuildApiContext("Lost", comments: "  ", actualCloseDate: today, lossReason: CrmSchema.Opportunity.LossReasonValues.Price),
                BuildCallerService(oppQualify)));
            AssertCrmError(CrmError.CRM014, () => ExecuteApi(
                BuildApiContext("Lost", comments: "Lost.", actualCloseDate: today.AddDays(1), lossReason: CrmSchema.Opportunity.LossReasonValues.Price),
                BuildCallerService(oppQualify)));
        }

        [Fact]
        public void TransitionOpportunity_ConcurrencyClosedDealSecurityAndTransactionNegatives_EnforceExactCodes()
        {
            var today = DateTime.UtcNow.Date;
            var openOpp = BuildOpenOpportunity(CrmSchema.Opportunity.StageValues.Qualify);

            // Stale or missing ExpectedRowVersion -> CRM021
            AssertCrmError(CrmError.CRM021, () => ExecuteApi(BuildApiContext("Advance", rowVersion: null), BuildCallerService(openOpp)));
            AssertCrmError(CrmError.CRM021, () => ExecuteApi(BuildApiContext("Advance", rowVersion: "W/\"9999\""), BuildCallerService(openOpp)));

            // Closed Opportunity -> CRM006 on Advance, CRM014 on Won/Lost
            var closedOpp = BuildOpenOpportunity(CrmSchema.Opportunity.StageValues.Close);
            closedOpp[CrmSchema.Opportunity.StateCode] = new OptionSetValue(CrmSchema.Opportunity.State.Inactive);
            closedOpp[CrmSchema.Opportunity.StatusCode] = new OptionSetValue(CrmSchema.Opportunity.Status.Won);

            AssertCrmError(CrmError.CRM006, () => ExecuteApi(BuildApiContext("Advance"), BuildCallerService(closedOpp)));
            AssertCrmError(CrmError.CRM014, () => ExecuteApi(BuildApiContext("Won", comments: "Again", actualCloseDate: today), BuildCallerService(closedOpp)));
            AssertCrmError(CrmError.CRM014, () => ExecuteApi(BuildApiContext("Lost", comments: "Again", actualCloseDate: today, lossReason: CrmSchema.Opportunity.LossReasonValues.Other), BuildCallerService(closedOpp)));

            // Caller lacking Write access -> CRM013
            AssertCrmError(CrmError.CRM013, () => ExecuteApi(
                BuildApiContext("Advance"),
                BuildCallerService(openOpp, oppRights: AccessRights.ReadAccess)));

            // Unconfigured Internal Writer -> CRM013 with ZERO writes
            var unconfiguredCaller = BuildCallerService(openOpp, configureWriter: false);
            AssertCrmError(CrmError.CRM013, () => ExecuteApi(BuildApiContext("Advance"), unconfiguredCaller));
            Assert.Empty(unconfiguredCaller.UpdatedEntities);
            Assert.DoesNotContain(unconfiguredCaller.ExecutedRequests, r => r is UpdateRequest);

            // Non-transactional context -> CRM006
            AssertCrmError(CrmError.CRM006, () => ExecuteApi(
                BuildApiContext("Advance", isInTransaction: false),
                BuildCallerService(openOpp)));
        }
    }
}
