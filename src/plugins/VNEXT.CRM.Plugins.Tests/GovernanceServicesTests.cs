using System;
using System.Collections.Generic;
using System.ServiceModel;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using VNEXT.CRM.Plugins.Core;
using VNEXT.CRM.Plugins.Domain;
using VNEXT.CRM.Plugins.Services;
using Xunit;

namespace VNEXT.CRM.Plugins.Tests
{
    internal sealed class RecordingOrganizationService : IOrganizationService
    {
        public Func<Entity, Guid> OnCreate { get; set; }
        public Func<string, Guid, ColumnSet, Entity> OnRetrieve { get; set; }
        public Action<Entity> OnUpdate { get; set; }
        public Func<QueryBase, EntityCollection> OnRetrieveMultiple { get; set; }

        public List<Entity> CreatedEntities { get; } = new List<Entity>();
        public List<Entity> UpdatedEntities { get; } = new List<Entity>();

        public Guid Create(Entity entity)
        {
            CreatedEntities.Add(entity);
            return OnCreate != null ? OnCreate(entity) : Guid.NewGuid();
        }

        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            return OnRetrieve != null ? OnRetrieve(entityName, id, columnSet) : null;
        }

        public void Update(Entity entity)
        {
            UpdatedEntities.Add(entity);
            if (OnUpdate != null)
            {
                OnUpdate(entity);
            }
        }

        public void Delete(string entityName, Guid id)
        {
            throw new NotSupportedException();
        }

        public OrganizationResponse Execute(OrganizationRequest request)
        {
            throw new NotSupportedException();
        }

        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        {
            throw new NotSupportedException();
        }

        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        {
            throw new NotSupportedException();
        }

        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            return OnRetrieveMultiple != null ? OnRetrieveMultiple(query) : new EntityCollection();
        }
    }

    public class GovernanceServicesTests
    {
        private static readonly Guid CorrelationId = new Guid("aaaaaaaa-1111-1111-1111-111111111111");
        private static readonly Guid BaseCurrencyId = new Guid("11111111-1111-1111-1111-111111111111");
        private static readonly Guid OppId = new Guid("bbbbbbbb-2222-2222-2222-222222222222");
        private static readonly Guid OtherOppId = new Guid("cccccccc-3333-3333-3333-333333333333");
        private static readonly Guid BpfId = new Guid("dddddddd-4444-4444-4444-444444444444");
        private static readonly Guid ApprovalId = new Guid("eeeeeeee-5555-5555-5555-555555555555");
        private static readonly Guid DealOwnerId = new Guid("10101010-1010-1010-1010-101010101010");
        private static readonly Guid ManagerId = new Guid("20202020-2020-2020-2020-202020202020");
        private static readonly Guid OtherManagerId = new Guid("30303030-3030-3030-3030-303030303030");
        private static readonly Guid SalesABuId = new Guid("40404040-4040-4040-4040-404040404040");
        private static readonly Guid SalesBBuId = new Guid("50505050-5050-5050-5050-505050505050");

        private static readonly CrmPolicyConfig DefaultConfig = CrmPolicyConfig.CreateDefaults(BaseCurrencyId);

        private static void AssertCrmError(string expectedCode, Action action)
        {
            var ex = Assert.Throws<InvalidPluginExecutionException>(action);
            Assert.StartsWith("[" + expectedCode + "] ", ex.Message);
            Assert.Contains("(CorrelationId: " + CorrelationId.ToString("D") + ")", ex.Message);
        }

        [Fact]
        public void BpfMirrorService_EnsureSingleQualifyInstance_CreatesOneWithCreateSafeFieldsOnly()
        {
            var svc = new RecordingOrganizationService
            {
                OnRetrieveMultiple = q => new EntityCollection(),
                OnCreate = e => BpfId
            };

            var longName = new string('X', 250);
            var createdId = BpfMirrorService.EnsureSingleQualifyInstance(svc, OppId, "  " + longName + "  ", CorrelationId);

            Assert.Equal(BpfId, createdId);
            Assert.Single(svc.CreatedEntities);
            Assert.Empty(svc.UpdatedEntities);

            var created = svc.CreatedEntities[0];
            Assert.Equal(CrmSchema.Entities.OpportunitySalesProcess, created.LogicalName);
            Assert.Equal(200, created.GetAttributeValue<string>(CrmSchema.Bpf.Name).Length);
            Assert.Equal(OppId, created.GetAttributeValue<EntityReference>(CrmSchema.Bpf.Opportunity).Id);
            Assert.Equal(CrmSchema.Bpf.WorkflowId, created.GetAttributeValue<EntityReference>(CrmSchema.Bpf.ProcessId).Id);
            Assert.Equal(CrmSchema.Bpf.StageQualifyId, created.GetAttributeValue<EntityReference>(CrmSchema.Bpf.ActiveStageId).Id);
            Assert.Equal(
                OpportunityLifecycleRules.BuildTraversedPathForStage(CrmSchema.Opportunity.StageValues.Qualify, CorrelationId),
                created.GetAttributeValue<string>(CrmSchema.Bpf.TraversedPath));

            // Create-unsafe fields MUST NOT be sent on Create
            Assert.False(created.Contains(CrmSchema.Bpf.StateCode));
            Assert.False(created.Contains(CrmSchema.Bpf.StatusCode));
            Assert.False(created.Contains(CrmSchema.Bpf.ActiveStageStartedOn));
        }

        [Fact]
        public void BpfMirrorService_EnsureSingleQualifyInstance_ReusesExistingAndRejectsDuplicates()
        {
            var qualifyPath = OpportunityLifecycleRules.BuildTraversedPathForStage(CrmSchema.Opportunity.StageValues.Qualify, CorrelationId);

            // Existing instance already at Qualify -> zero Create, zero Update
            var existingClean = new Entity(CrmSchema.Entities.OpportunitySalesProcess, BpfId);
            existingClean[CrmSchema.Bpf.ActiveStageId] = new EntityReference(CrmSchema.Entities.ProcessStage, CrmSchema.Bpf.StageQualifyId);
            existingClean[CrmSchema.Bpf.TraversedPath] = qualifyPath;

            var reuseNoOpSvc = new RecordingOrganizationService
            {
                OnRetrieveMultiple = q => new EntityCollection(new[] { existingClean })
            };
            var reusedId = BpfMirrorService.EnsureSingleQualifyInstance(reuseNoOpSvc, OppId, "Deal A", CorrelationId);
            Assert.Equal(BpfId, reusedId);
            Assert.Empty(reuseNoOpSvc.CreatedEntities);
            Assert.Empty(reuseNoOpSvc.UpdatedEntities);

            // Existing instance with drifted stage/path -> updates ONLY activestageid & traversedpath, zero Create
            var existingDrifted = new Entity(CrmSchema.Entities.OpportunitySalesProcess, BpfId);
            existingDrifted[CrmSchema.Bpf.ActiveStageId] = new EntityReference(CrmSchema.Entities.ProcessStage, CrmSchema.Bpf.StageDevelopId);
            existingDrifted[CrmSchema.Bpf.TraversedPath] = "drifted";

            var reuseFixSvc = new RecordingOrganizationService
            {
                OnRetrieveMultiple = q => new EntityCollection(new[] { existingDrifted })
            };
            var fixedId = BpfMirrorService.EnsureSingleQualifyInstance(reuseFixSvc, OppId, "Deal A", CorrelationId);
            Assert.Equal(BpfId, fixedId);
            Assert.Empty(reuseFixSvc.CreatedEntities);
            Assert.Single(reuseFixSvc.UpdatedEntities);
            var patch = reuseFixSvc.UpdatedEntities[0];
            Assert.Equal(2, patch.Attributes.Count);
            Assert.Equal(CrmSchema.Bpf.StageQualifyId, patch.GetAttributeValue<EntityReference>(CrmSchema.Bpf.ActiveStageId).Id);
            Assert.Equal(qualifyPath, patch.GetAttributeValue<string>(CrmSchema.Bpf.TraversedPath));

            // > 1 instances -> CRM006
            var duplicateSvc = new RecordingOrganizationService
            {
                OnRetrieveMultiple = q => new EntityCollection(new[] { existingClean, new Entity(CrmSchema.Entities.OpportunitySalesProcess, Guid.NewGuid()) })
            };
            AssertCrmError(CrmError.CRM006, () => BpfMirrorService.EnsureSingleQualifyInstance(duplicateSvc, OppId, "Deal A", CorrelationId));
        }

        [Fact]
        public void BpfMirrorService_MirrorStageAdvanceAndClose_UpdatesExactFieldsAndRequiresSingleInstance()
        {
            var singleBpf = new Entity(CrmSchema.Entities.OpportunitySalesProcess, BpfId);

            // Advance to Develop, Propose, Close -> ONLY activestageid and traversedpath updated
            foreach (var stage in new[]
            {
                CrmSchema.Opportunity.StageValues.Develop,
                CrmSchema.Opportunity.StageValues.Propose,
                CrmSchema.Opportunity.StageValues.Close
            })
            {
                var svc = new RecordingOrganizationService
                {
                    OnRetrieveMultiple = q => new EntityCollection(new[] { singleBpf })
                };

                BpfMirrorService.MirrorStageAdvance(svc, OppId, stage, CorrelationId);

                Assert.Single(svc.UpdatedEntities);
                var patch = svc.UpdatedEntities[0];
                Assert.Equal(BpfId, patch.Id);
                Assert.Equal(2, patch.Attributes.Count);
                Assert.Equal(
                    OpportunityLifecycleRules.GetBpfStageIdForOpportunityStage(stage, CorrelationId),
                    patch.GetAttributeValue<EntityReference>(CrmSchema.Bpf.ActiveStageId).Id);
                Assert.Equal(
                    OpportunityLifecycleRules.BuildTraversedPathForStage(stage, CorrelationId),
                    patch.GetAttributeValue<string>(CrmSchema.Bpf.TraversedPath));
            }

            // Close Won -> ONLY statecode = 1, statuscode = 2 (Finished)
            var wonSvc = new RecordingOrganizationService
            {
                OnRetrieveMultiple = q => new EntityCollection(new[] { singleBpf })
            };
            BpfMirrorService.MirrorClose(wonSvc, OppId, true, CorrelationId);
            Assert.Single(wonSvc.UpdatedEntities);
            Assert.Equal(2, wonSvc.UpdatedEntities[0].Attributes.Count);
            Assert.Equal(CrmSchema.Bpf.StateInactive, wonSvc.UpdatedEntities[0].GetAttributeValue<OptionSetValue>(CrmSchema.Bpf.StateCode).Value);
            Assert.Equal(CrmSchema.Bpf.StatusFinished, wonSvc.UpdatedEntities[0].GetAttributeValue<OptionSetValue>(CrmSchema.Bpf.StatusCode).Value);

            // Close Lost -> ONLY statecode = 1, statuscode = 3 (Aborted)
            var lostSvc = new RecordingOrganizationService
            {
                OnRetrieveMultiple = q => new EntityCollection(new[] { singleBpf })
            };
            BpfMirrorService.MirrorClose(lostSvc, OppId, false, CorrelationId);
            Assert.Single(lostSvc.UpdatedEntities);
            Assert.Equal(2, lostSvc.UpdatedEntities[0].Attributes.Count);
            Assert.Equal(CrmSchema.Bpf.StateInactive, lostSvc.UpdatedEntities[0].GetAttributeValue<OptionSetValue>(CrmSchema.Bpf.StateCode).Value);
            Assert.Equal(CrmSchema.Bpf.StatusAborted, lostSvc.UpdatedEntities[0].GetAttributeValue<OptionSetValue>(CrmSchema.Bpf.StatusCode).Value);

            // 0 instances -> CRM006 on Advance and Close
            var emptySvc = new RecordingOrganizationService
            {
                OnRetrieveMultiple = q => new EntityCollection()
            };
            AssertCrmError(CrmError.CRM006, () => BpfMirrorService.MirrorStageAdvance(emptySvc, OppId, CrmSchema.Opportunity.StageValues.Develop, CorrelationId));
            AssertCrmError(CrmError.CRM006, () => BpfMirrorService.MirrorClose(emptySvc, OppId, true, CorrelationId));

            // > 1 instances -> CRM006 on Advance and Close
            var dupSvc = new RecordingOrganizationService
            {
                OnRetrieveMultiple = q => new EntityCollection(new[] { singleBpf, new Entity(CrmSchema.Entities.OpportunitySalesProcess, Guid.NewGuid()) })
            };
            AssertCrmError(CrmError.CRM006, () => BpfMirrorService.MirrorStageAdvance(dupSvc, OppId, CrmSchema.Opportunity.StageValues.Develop, CorrelationId));
            AssertCrmError(CrmError.CRM006, () => BpfMirrorService.MirrorClose(dupSvc, OppId, false, CorrelationId));
        }

        [Fact]
        public void ApprovalGovernanceService_InvalidateCurrentApprovalOnLineChange_HandlesPendingApprovedRejectedAndErrors()
        {
            // Null pointer -> false, zero writes
            var noopSvc = new RecordingOrganizationService();
            Assert.False(ApprovalGovernanceService.InvalidateCurrentApprovalOnLineChange(noopSvc, OppId, null, CorrelationId));
            Assert.Empty(noopSvc.UpdatedEntities);

            // Pending -> marks Superseded (100000003) AND clears vn_currentapproval = null
            var pendingSvc = CreateApprovalInvalidationService(OppId, CrmSchema.ApprovalRequest.ResultValues.Pending);
            Assert.True(ApprovalGovernanceService.InvalidateCurrentApprovalOnLineChange(
                pendingSvc,
                OppId,
                new EntityReference(CrmSchema.Entities.ApprovalRequest, ApprovalId),
                CorrelationId));
            Assert.Equal(2, pendingSvc.UpdatedEntities.Count);
            Assert.Equal(CrmSchema.Entities.ApprovalRequest, pendingSvc.UpdatedEntities[0].LogicalName);
            Assert.Equal(CrmSchema.ApprovalRequest.ResultValues.Superseded, pendingSvc.UpdatedEntities[0].GetAttributeValue<OptionSetValue>(CrmSchema.ApprovalRequest.Result).Value);
            Assert.Equal(CrmSchema.Entities.Opportunity, pendingSvc.UpdatedEntities[1].LogicalName);
            Assert.Null(pendingSvc.UpdatedEntities[1][CrmSchema.Opportunity.CurrentApproval]);

            // Approved -> marks Superseded (100000003) AND clears vn_currentapproval = null
            var approvedSvc = CreateApprovalInvalidationService(OppId, CrmSchema.ApprovalRequest.ResultValues.Approved);
            Assert.True(ApprovalGovernanceService.InvalidateCurrentApprovalOnLineChange(
                approvedSvc,
                OppId,
                new EntityReference(CrmSchema.Entities.ApprovalRequest, ApprovalId),
                CorrelationId));
            Assert.Equal(2, approvedSvc.UpdatedEntities.Count);
            Assert.Equal(CrmSchema.Entities.ApprovalRequest, approvedSvc.UpdatedEntities[0].LogicalName);
            Assert.Equal(CrmSchema.ApprovalRequest.ResultValues.Superseded, approvedSvc.UpdatedEntities[0].GetAttributeValue<OptionSetValue>(CrmSchema.ApprovalRequest.Result).Value);
            Assert.Equal(CrmSchema.Entities.Opportunity, approvedSvc.UpdatedEntities[1].LogicalName);
            Assert.Null(approvedSvc.UpdatedEntities[1][CrmSchema.Opportunity.CurrentApproval]);

            // Rejected -> MUST NOT modify vn_approvalrequest, ONLY clears vn_currentapproval = null (BR19 & R-02)
            var rejectedSvc = CreateApprovalInvalidationService(OppId, CrmSchema.ApprovalRequest.ResultValues.Rejected);
            Assert.True(ApprovalGovernanceService.InvalidateCurrentApprovalOnLineChange(
                rejectedSvc,
                OppId,
                new EntityReference(CrmSchema.Entities.ApprovalRequest, ApprovalId),
                CorrelationId));
            Assert.Single(rejectedSvc.UpdatedEntities);
            Assert.Equal(CrmSchema.Entities.Opportunity, rejectedSvc.UpdatedEntities[0].LogicalName);
            Assert.True(rejectedSvc.UpdatedEntities[0].Contains(CrmSchema.Opportunity.CurrentApproval));
            Assert.Null(rejectedSvc.UpdatedEntities[0][CrmSchema.Opportunity.CurrentApproval]);

            // Mismatched vn_opportunity -> CRM012 without clearing pointer
            var wrongOppSvc = CreateApprovalInvalidationService(OtherOppId, CrmSchema.ApprovalRequest.ResultValues.Pending);
            AssertCrmError(CrmError.CRM012, () => ApprovalGovernanceService.InvalidateCurrentApprovalOnLineChange(
                wrongOppSvc,
                OppId,
                new EntityReference(CrmSchema.Entities.ApprovalRequest, ApprovalId),
                CorrelationId));
            Assert.Empty(wrongOppSvc.UpdatedEntities);

            // Unused/invalid vn_result (100000004) -> CRM012 without clearing pointer
            var invalidResultSvc = CreateApprovalInvalidationService(OppId, 100000004);
            AssertCrmError(CrmError.CRM012, () => ApprovalGovernanceService.InvalidateCurrentApprovalOnLineChange(
                invalidResultSvc,
                OppId,
                new EntityReference(CrmSchema.Entities.ApprovalRequest, ApprovalId),
                CorrelationId));
            Assert.Empty(invalidResultSvc.UpdatedEntities);
        }

        [Fact]
        public void ApprovalGovernanceService_ValidateCurrentApprovalForWon_PassesBelowThresholdAndValidApprovedAboveThreshold()
        {
            // Below BR20 threshold (49999.99, 10.00%) -> passes with null pointer and zero service calls
            var oppNoApproval = new Entity(CrmSchema.Entities.Opportunity, OppId);
            oppNoApproval[CrmSchema.Opportunity.OwnerId] = new EntityReference(CrmSchema.Entities.SystemUser, DealOwnerId);
            ApprovalGovernanceService.ValidateCurrentApprovalForWon(
                new RecordingOrganizationService(),
                oppNoApproval,
                49999.99m,
                10.00m,
                DefaultConfig,
                CorrelationId);

            // Above BR20 threshold (Total = 50000.00) with fully valid Approved Request -> passes
            var oppWithApproval = BuildOpportunity(OppId, DealOwnerId, ApprovalId);
            var validReq = BuildValidApprovedRequest(50000.00m, 5.00m);
            var validReader = BuildWonReaderService(validReq, approverDisabled: false, approverBuId: SalesABuId, ownerBuId: SalesABuId);

            ApprovalGovernanceService.ValidateCurrentApprovalForWon(
                validReader,
                oppWithApproval,
                50000.00m,
                5.00m,
                DefaultConfig,
                CorrelationId);

            // Above BR20 threshold via MaxDiscount = 10.01% -> passes when snapshot matches
            var discountReq = BuildValidApprovedRequest(1000.00m, 10.01m);
            var discountReader = BuildWonReaderService(discountReq, approverDisabled: false, approverBuId: SalesABuId, ownerBuId: SalesABuId);
            ApprovalGovernanceService.ValidateCurrentApprovalForWon(
                discountReader,
                oppWithApproval,
                1000.00m,
                10.01m,
                DefaultConfig,
                CorrelationId);
        }

        [Fact]
        public void ApprovalGovernanceService_ValidateCurrentApprovalForWon_RejectsInvalidSnapshotsStatesAndBuIntegrity()
        {
            var opp = BuildOpportunity(OppId, DealOwnerId, ApprovalId);

            // Null pointer when approval required -> CRM012
            var oppNoPointer = BuildOpportunity(OppId, DealOwnerId, null);
            AssertCrmError(CrmError.CRM012, () => ApprovalGovernanceService.ValidateCurrentApprovalForWon(
                new RecordingOrganizationService(),
                oppNoPointer,
                50000.00m,
                0m,
                DefaultConfig,
                CorrelationId));

            // Non-Approved results (Pending, Rejected, Superseded, Cancelled) -> CRM012
            foreach (var badResult in new[]
            {
                CrmSchema.ApprovalRequest.ResultValues.Pending,
                CrmSchema.ApprovalRequest.ResultValues.Rejected,
                CrmSchema.ApprovalRequest.ResultValues.Superseded,
                CrmSchema.ApprovalRequest.ResultValues.Cancelled
            })
            {
                var req = BuildValidApprovedRequest(50000.00m, 5.00m);
                req[CrmSchema.ApprovalRequest.Result] = new OptionSetValue(badResult);
                var reader = BuildWonReaderService(req, false, SalesABuId, SalesABuId);
                AssertCrmError(CrmError.CRM012, () => ApprovalGovernanceService.ValidateCurrentApprovalForWon(
                    reader, opp, 50000.00m, 5.00m, DefaultConfig, CorrelationId));
            }

            // Missing respondedon -> CRM012
            var noRespondedOn = BuildValidApprovedRequest(50000.00m, 5.00m);
            noRespondedOn.Attributes.Remove(CrmSchema.ApprovalRequest.RespondedOn);
            AssertCrmError(CrmError.CRM012, () => ApprovalGovernanceService.ValidateCurrentApprovalForWon(
                BuildWonReaderService(noRespondedOn, false, SalesABuId, SalesABuId),
                opp, 50000.00m, 5.00m, DefaultConfig, CorrelationId));

            // Wrong Total -> CRM012
            var wrongTotal = BuildValidApprovedRequest(49999.00m, 5.00m);
            AssertCrmError(CrmError.CRM012, () => ApprovalGovernanceService.ValidateCurrentApprovalForWon(
                BuildWonReaderService(wrongTotal, false, SalesABuId, SalesABuId),
                opp, 50000.00m, 5.00m, DefaultConfig, CorrelationId));

            // Wrong MaxDiscount -> CRM012
            var wrongDiscount = BuildValidApprovedRequest(50000.00m, 4.99m);
            AssertCrmError(CrmError.CRM012, () => ApprovalGovernanceService.ValidateCurrentApprovalForWon(
                BuildWonReaderService(wrongDiscount, false, SalesABuId, SalesABuId),
                opp, 50000.00m, 5.00m, DefaultConfig, CorrelationId));

            // Wrong DealOwner snapshot -> CRM012
            var wrongOwner = BuildValidApprovedRequest(50000.00m, 5.00m);
            wrongOwner[CrmSchema.ApprovalRequest.DealOwner] = new EntityReference(CrmSchema.Entities.SystemUser, OtherManagerId);
            AssertCrmError(CrmError.CRM012, () => ApprovalGovernanceService.ValidateCurrentApprovalForWon(
                BuildWonReaderService(wrongOwner, false, SalesABuId, SalesABuId),
                opp, 50000.00m, 5.00m, DefaultConfig, CorrelationId));

            // Self-approved (approver == owner) -> CRM012
            var selfApproved = BuildValidApprovedRequest(50000.00m, 5.00m);
            selfApproved[CrmSchema.ApprovalRequest.Approver] = new EntityReference(CrmSchema.Entities.SystemUser, DealOwnerId);
            selfApproved[CrmSchema.ApprovalRequest.Responder] = new EntityReference(CrmSchema.Entities.SystemUser, DealOwnerId);
            AssertCrmError(CrmError.CRM012, () => ApprovalGovernanceService.ValidateCurrentApprovalForWon(
                BuildWonReaderService(selfApproved, false, SalesABuId, SalesABuId),
                opp, 50000.00m, 5.00m, DefaultConfig, CorrelationId));

            // Responder != Approver -> CRM012
            var wrongResponder = BuildValidApprovedRequest(50000.00m, 5.00m);
            wrongResponder[CrmSchema.ApprovalRequest.Responder] = new EntityReference(CrmSchema.Entities.SystemUser, OtherManagerId);
            AssertCrmError(CrmError.CRM012, () => ApprovalGovernanceService.ValidateCurrentApprovalForWon(
                BuildWonReaderService(wrongResponder, false, SalesABuId, SalesABuId),
                opp, 50000.00m, 5.00m, DefaultConfig, CorrelationId));

            // Disabled approver -> CRM012
            var validReq = BuildValidApprovedRequest(50000.00m, 5.00m);
            AssertCrmError(CrmError.CRM012, () => ApprovalGovernanceService.ValidateCurrentApprovalForWon(
                BuildWonReaderService(validReq, approverDisabled: true, approverBuId: SalesABuId, ownerBuId: SalesABuId),
                opp, 50000.00m, 5.00m, DefaultConfig, CorrelationId));

            // Cross-BU approver (Sales B Manager approving Sales A Rep) -> CRM012
            AssertCrmError(CrmError.CRM012, () => ApprovalGovernanceService.ValidateCurrentApprovalForWon(
                BuildWonReaderService(validReq, approverDisabled: false, approverBuId: SalesBBuId, ownerBuId: SalesABuId),
                opp, 50000.00m, 5.00m, DefaultConfig, CorrelationId));

            // Retrieve fault on ApprovalRequest -> CRM012
            var faultingReader = new RecordingOrganizationService
            {
                OnRetrieve = (entity, id, cols) => throw new FaultException<OrganizationServiceFault>(
                    new OrganizationServiceFault { ErrorCode = -2147220969, Message = "Not found" },
                    new FaultReason("Not found"))
            };
            AssertCrmError(CrmError.CRM012, () => ApprovalGovernanceService.ValidateCurrentApprovalForWon(
                faultingReader, opp, 50000.00m, 5.00m, DefaultConfig, CorrelationId));
        }

        private static RecordingOrganizationService CreateApprovalInvalidationService(Guid requestOppId, int resultValue)
        {
            return new RecordingOrganizationService
            {
                OnRetrieve = (entity, id, cols) =>
                {
                    if (entity == CrmSchema.Entities.ApprovalRequest && id == ApprovalId)
                    {
                        var req = new Entity(CrmSchema.Entities.ApprovalRequest, ApprovalId);
                        req[CrmSchema.ApprovalRequest.Opportunity] = new EntityReference(CrmSchema.Entities.Opportunity, requestOppId);
                        req[CrmSchema.ApprovalRequest.Result] = new OptionSetValue(resultValue);
                        return req;
                    }

                    return null;
                }
            };
        }

        private static Entity BuildOpportunity(Guid oppId, Guid ownerId, Guid? currentApprovalId)
        {
            var opp = new Entity(CrmSchema.Entities.Opportunity, oppId);
            opp[CrmSchema.Opportunity.OwnerId] = new EntityReference(CrmSchema.Entities.SystemUser, ownerId);
            if (currentApprovalId.HasValue)
            {
                opp[CrmSchema.Opportunity.CurrentApproval] = new EntityReference(CrmSchema.Entities.ApprovalRequest, currentApprovalId.Value);
            }

            return opp;
        }

        private static Entity BuildValidApprovedRequest(decimal total, decimal maxDiscount)
        {
            var req = new Entity(CrmSchema.Entities.ApprovalRequest, ApprovalId);
            req[CrmSchema.ApprovalRequest.Opportunity] = new EntityReference(CrmSchema.Entities.Opportunity, OppId);
            req[CrmSchema.ApprovalRequest.Result] = new OptionSetValue(CrmSchema.ApprovalRequest.ResultValues.Approved);
            req[CrmSchema.ApprovalRequest.DealOwner] = new EntityReference(CrmSchema.Entities.SystemUser, DealOwnerId);
            req[CrmSchema.ApprovalRequest.Approver] = new EntityReference(CrmSchema.Entities.SystemUser, ManagerId);
            req[CrmSchema.ApprovalRequest.Responder] = new EntityReference(CrmSchema.Entities.SystemUser, ManagerId);
            req[CrmSchema.ApprovalRequest.RespondedOn] = new DateTime(2026, 11, 23, 10, 0, 0, DateTimeKind.Utc);
            req[CrmSchema.ApprovalRequest.Total] = new Money(total);
            req[CrmSchema.ApprovalRequest.MaxDiscount] = maxDiscount;
            return req;
        }

        private static RecordingOrganizationService BuildWonReaderService(
            Entity approvalRequest,
            bool approverDisabled,
            Guid approverBuId,
            Guid ownerBuId)
        {
            return new RecordingOrganizationService
            {
                OnRetrieve = (entity, id, cols) =>
                {
                    if (entity == CrmSchema.Entities.ApprovalRequest && id == ApprovalId)
                    {
                        return approvalRequest;
                    }

                    if (entity == CrmSchema.Entities.SystemUser)
                    {
                        var user = new Entity(CrmSchema.Entities.SystemUser, id);
                        if (id == ManagerId)
                        {
                            user[CrmSchema.Platform.IsDisabled] = approverDisabled;
                            user[CrmSchema.Platform.BusinessUnitId] = new EntityReference(CrmSchema.Entities.BusinessUnit, approverBuId);
                            return user;
                        }

                        if (id == DealOwnerId)
                        {
                            user[CrmSchema.Platform.IsDisabled] = false;
                            user[CrmSchema.Platform.BusinessUnitId] = new EntityReference(CrmSchema.Entities.BusinessUnit, ownerBuId);
                            return user;
                        }
                    }

                    return null;
                }
            };
        }
    }
}
