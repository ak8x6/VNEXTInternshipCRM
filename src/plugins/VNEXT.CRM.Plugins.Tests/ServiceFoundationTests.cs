using System;
using System.Collections.Generic;
using System.ServiceModel;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using VNEXT.CRM.Plugins.Core;
using VNEXT.CRM.Plugins.Services;
using Xunit;

namespace VNEXT.CRM.Plugins.Tests
{
    public sealed class FakePluginExecutionContext : IPluginExecutionContext
    {
        public int Mode { get; set; }
        public int IsolationMode { get; set; } = 2;
        public int Depth { get; set; } = 1;
        public string MessageName { get; set; } = string.Empty;
        public string PrimaryEntityName { get; set; } = string.Empty;
        public Guid? RequestId { get; set; } = Guid.NewGuid();
        public string SecondaryEntityName { get; set; } = string.Empty;
        public ParameterCollection InputParameters { get; set; } = new ParameterCollection();
        public ParameterCollection OutputParameters { get; set; } = new ParameterCollection();
        public ParameterCollection SharedVariables { get; set; } = new ParameterCollection();
        public Guid UserId { get; set; }
        public Guid InitiatingUserId { get; set; }
        public Guid BusinessUnitId { get; set; }
        public Guid OrganizationId { get; set; }
        public string OrganizationName { get; set; } = "org3b306249";
        public Guid PrimaryEntityId { get; set; }
        public EntityImageCollection PreEntityImages { get; set; } = new EntityImageCollection();
        public EntityImageCollection PostEntityImages { get; set; } = new EntityImageCollection();
        public EntityReference OwningExtension { get; set; }
        public Guid CorrelationId { get; set; } = Guid.NewGuid();
        public bool IsExecutingOffline { get; set; }
        public bool IsOfflinePlayback { get; set; }
        public bool IsInTransaction { get; set; } = true;
        public Guid OperationId { get; set; } = Guid.NewGuid();
        public DateTime OperationCreatedOn { get; set; } = DateTime.UtcNow;
        public int Stage { get; set; } = 30;
        public IPluginExecutionContext ParentContext { get; set; }
    }

    public sealed class FakeOrganizationService : IOrganizationService
    {
        public Func<string, Guid, ColumnSet, Entity> OnRetrieve { get; set; }
        public Func<QueryBase, EntityCollection> OnRetrieveMultiple { get; set; }
        public Func<OrganizationRequest, OrganizationResponse> OnExecute { get; set; }

        public List<OrganizationRequest> ExecutedRequests { get; } = new List<OrganizationRequest>();
        public List<QueryBase> ExecutedQueries { get; } = new List<QueryBase>();

        public Guid Create(Entity entity)
        {
            throw new NotSupportedException();
        }

        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            if (OnRetrieve != null)
            {
                return OnRetrieve(entityName, id, columnSet);
            }

            return null;
        }

        public void Update(Entity entity)
        {
            throw new NotSupportedException();
        }

        public void Delete(string entityName, Guid id)
        {
            throw new NotSupportedException();
        }

        public OrganizationResponse Execute(OrganizationRequest request)
        {
            ExecutedRequests.Add(request);
            if (OnExecute != null)
            {
                return OnExecute(request);
            }

            return new OrganizationResponse();
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
            ExecutedQueries.Add(query);
            if (OnRetrieveMultiple != null)
            {
                return OnRetrieveMultiple(query);
            }

            return new EntityCollection();
        }
    }

    public sealed class FakeOrganizationServiceFactory : IOrganizationServiceFactory
    {
        private readonly Func<Guid?, IOrganizationService> _resolver;

        public List<Guid?> RequestedUserIds { get; } = new List<Guid?>();

        public FakeOrganizationServiceFactory(Func<Guid?, IOrganizationService> resolver)
        {
            _resolver = resolver;
        }

        public IOrganizationService CreateOrganizationService(Guid? userId)
        {
            RequestedUserIds.Add(userId);
            return _resolver != null ? _resolver(userId) : null;
        }
    }

    public class ServiceFoundationTests
    {
        private static readonly Guid CorrelationId = new Guid("44444444-4444-4444-4444-444444444444");
        private static readonly Guid BaseCurrencyId = new Guid("11111111-1111-1111-1111-111111111111");
        private static readonly Guid CallerUserId = new Guid("55555555-5555-5555-5555-555555555555");
        private static readonly Guid WriterUserId = new Guid("66666666-6666-6666-6666-666666666666");
        private static readonly Guid OtherUserId = new Guid("77777777-7777-7777-7777-777777777777");
        private static readonly Guid BoundLeadId = new Guid("88888888-8888-8888-8888-888888888888");
        private static readonly Guid BoundOppId = new Guid("99999999-9999-9999-9999-999999999999");

        private static void AssertCrmError(string expectedCode, Action action)
        {
            var ex = Assert.Throws<InvalidPluginExecutionException>(action);
            Assert.StartsWith("[" + expectedCode + "] ", ex.Message);
            Assert.Contains("(CorrelationId: " + CorrelationId.ToString("D") + ")", ex.Message);
        }

        private static FaultException<OrganizationServiceFault> CreateFault(int errorCode, string message)
        {
            var fault = new OrganizationServiceFault
            {
                ErrorCode = errorCode,
                Message = message
            };
            return new FaultException<OrganizationServiceFault>(fault, new FaultReason(message));
        }

        [Theory]
        [InlineData("12345", "12345")]
        [InlineData("  12345  ", "12345")]
        [InlineData("W/\"12345\"", "12345")]
        [InlineData("w/\"987\"", "987")]
        [InlineData("\"555\"", "555")]
        [InlineData("  W/\" 2809269 \"  ", "2809269")]
        public void ConcurrencyService_NormalizeRowVersion_StripsWeakEtagAndQuotes(string raw, string expected)
        {
            Assert.Equal(expected, ConcurrencyService.NormalizeRowVersion(raw, CorrelationId));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("W/\"\"")]
        [InlineData("\"   \"")]
        [InlineData("w/")]
        public void ConcurrencyService_NormalizeRowVersion_RejectsBlankOrEmptyWithCrm021(string badRaw)
        {
            AssertCrmError(CrmError.CRM021, () => ConcurrencyService.NormalizeRowVersion(badRaw, CorrelationId));
        }

        [Fact]
        public void ConcurrencyService_EnsureSynchronousTransaction_EnforcesMode0AndIsInTransaction()
        {
            var valid = new FakePluginExecutionContext
            {
                Mode = 0,
                IsInTransaction = true,
                CorrelationId = CorrelationId
            };
            ConcurrencyService.EnsureSynchronousTransaction(valid);

            var asyncCtx = new FakePluginExecutionContext
            {
                Mode = 1,
                IsInTransaction = true,
                CorrelationId = CorrelationId
            };
            AssertCrmError(CrmError.CRM006, () => ConcurrencyService.EnsureSynchronousTransaction(asyncCtx));

            var nonTxCtx = new FakePluginExecutionContext
            {
                Mode = 0,
                IsInTransaction = false,
                CorrelationId = CorrelationId
            };
            AssertCrmError(CrmError.CRM006, () => ConcurrencyService.EnsureSynchronousTransaction(nonTxCtx));
        }

        [Fact]
        public void ConcurrencyService_BumpOpportunityCommandSequence_SendsConditionalUpdateAndMapsConflicts()
        {
            var service = new FakeOrganizationService
            {
                OnExecute = req => new UpdateResponse()
            };

            var next = ConcurrencyService.BumpOpportunityCommandSequence(
                service,
                BoundOppId,
                "W/\"2809269\"",
                4,
                CorrelationId);

            Assert.Equal(5, next);
            Assert.Single(service.ExecutedRequests);

            var updateReq = Assert.IsType<UpdateRequest>(service.ExecutedRequests[0]);
            Assert.Equal(ConcurrencyBehavior.IfRowVersionMatches, updateReq.ConcurrencyBehavior);
            Assert.Equal(CrmSchema.Entities.Opportunity, updateReq.Target.LogicalName);
            Assert.Equal(BoundOppId, updateReq.Target.Id);
            Assert.Equal("2809269", updateReq.Target.RowVersion);
            Assert.Equal(5, updateReq.Target.GetAttributeValue<int>(CrmSchema.Opportunity.CommandSequence));

            // ConcurrencyVersionMismatch (-2147088254) -> CRM021
            var mismatchService = new FakeOrganizationService
            {
                OnExecute = req => throw CreateFault(-2147088254, "Version mismatch")
            };
            AssertCrmError(
                CrmError.CRM021,
                () => ConcurrencyService.BumpOpportunityCommandSequence(mismatchService, BoundOppId, "100", 1, CorrelationId));

            // ConcurrencyVersionNotProvided (-2147088253) -> CRM021
            var missingVersionService = new FakeOrganizationService
            {
                OnExecute = req => throw CreateFault(-2147088253, "Version not provided")
            };
            AssertCrmError(
                CrmError.CRM021,
                () => ConcurrencyService.BumpOpportunityCommandSequence(missingVersionService, BoundOppId, "100", 1, CorrelationId));

            // Non-concurrency fault re-thrown untouched
            var otherFaultService = new FakeOrganizationService
            {
                OnExecute = req => throw CreateFault(-2147220960, "Principal user is missing privilege")
            };
            var rethrown = Assert.Throws<FaultException<OrganizationServiceFault>>(
                () => ConcurrencyService.BumpOpportunityCommandSequence(otherFaultService, BoundOppId, "100", 1, CorrelationId));
            Assert.Equal(-2147220960, rethrown.Detail.ErrorCode);
        }

        [Fact]
        public void TrustedOperationScope_TryResolveFromParentChain_ResolvesAllFourApisAcrossMultiLevelChain()
        {
            // EXT10 QualifyLead (multi-level chain: API -> Update -> nested Create executed by Internal Writer)
            var apiExt10 = new FakePluginExecutionContext
            {
                MessageName = CrmSchema.Apis.QualifyLead,
                Mode = 0,
                IsInTransaction = true,
                Stage = 30,
                CorrelationId = CorrelationId,
                InitiatingUserId = CallerUserId,
                UserId = CallerUserId,
                InputParameters = new ParameterCollection
                {
                    { CrmSchema.Apis.Params.Target, new EntityReference(CrmSchema.Entities.Lead, BoundLeadId) }
                }
            };
            var middleCtx = new FakePluginExecutionContext
            {
                MessageName = "Create",
                Mode = 0,
                IsInTransaction = true,
                Stage = 20,
                Depth = 2,
                CorrelationId = CorrelationId,
                InitiatingUserId = CallerUserId,
                UserId = CallerUserId,
                ParentContext = apiExt10
            };
            var nestedBpfCreate = new FakePluginExecutionContext
            {
                MessageName = "Create",
                Mode = 0,
                IsInTransaction = true,
                Stage = 20,
                Depth = 3,
                CorrelationId = CorrelationId,
                InitiatingUserId = CallerUserId,
                UserId = WriterUserId,
                ParentContext = middleCtx
            };

            TrustedApiScope scope;
            Assert.True(TrustedOperationScope.TryResolveFromParentChain(nestedBpfCreate, out scope));
            Assert.Equal(CrmSchema.Apis.QualifyLead, scope.ApiMessage);
            Assert.Equal(CrmSchema.Entities.Lead, scope.BoundEntityLogicalName);
            Assert.Equal(BoundLeadId, scope.BoundEntityId);
            Assert.Null(scope.SubOperation);
            Assert.Equal(CallerUserId, scope.InitiatingUserId);
            Assert.Equal(CorrelationId, scope.CorrelationId);

            // EXT11 TransitionOpportunity (Command)
            var apiExt11 = new FakePluginExecutionContext
            {
                MessageName = CrmSchema.Apis.TransitionOpportunity,
                Mode = 0,
                IsInTransaction = true,
                Stage = 30,
                CorrelationId = CorrelationId,
                InitiatingUserId = CallerUserId,
                UserId = CallerUserId,
                InputParameters = new ParameterCollection
                {
                    { CrmSchema.Apis.Params.Target, new EntityReference(CrmSchema.Entities.Opportunity, BoundOppId) },
                    { CrmSchema.Apis.Params.Command, "Advance" }
                }
            };
            var nestedExt11 = new FakePluginExecutionContext
            {
                MessageName = "Update",
                Mode = 0,
                IsInTransaction = true,
                Stage = 20,
                Depth = 2,
                CorrelationId = CorrelationId,
                InitiatingUserId = CallerUserId,
                UserId = WriterUserId,
                ParentContext = apiExt11
            };
            Assert.True(TrustedOperationScope.TryResolveFromParentChain(nestedExt11, out scope));
            Assert.Equal(CrmSchema.Apis.TransitionOpportunity, scope.ApiMessage);
            Assert.Equal(CrmSchema.Entities.Opportunity, scope.BoundEntityLogicalName);
            Assert.Equal(BoundOppId, scope.BoundEntityId);
            Assert.Equal("Advance", scope.SubOperation);

            // EXT12 EditOpportunityLine (Operation)
            var apiExt12 = new FakePluginExecutionContext
            {
                MessageName = CrmSchema.Apis.EditOpportunityLine,
                Mode = 0,
                IsInTransaction = true,
                Stage = 30,
                CorrelationId = CorrelationId,
                InitiatingUserId = CallerUserId,
                InputParameters = new ParameterCollection
                {
                    { CrmSchema.Apis.Params.Target, new EntityReference(CrmSchema.Entities.Opportunity, BoundOppId) },
                    { CrmSchema.Apis.Params.Operation, "Add" }
                }
            };
            var nestedExt12 = new FakePluginExecutionContext
            {
                MessageName = "Create",
                Mode = 0,
                IsInTransaction = true,
                Stage = 20,
                Depth = 2,
                CorrelationId = CorrelationId,
                InitiatingUserId = CallerUserId,
                ParentContext = apiExt12
            };
            Assert.True(TrustedOperationScope.TryResolveFromParentChain(nestedExt12, out scope));
            Assert.Equal(CrmSchema.Apis.EditOpportunityLine, scope.ApiMessage);
            Assert.Equal("Add", scope.SubOperation);

            // EXT13 ManageDealApproval (Operation)
            var apiExt13 = new FakePluginExecutionContext
            {
                MessageName = CrmSchema.Apis.ManageDealApproval,
                Mode = 0,
                IsInTransaction = true,
                Stage = 30,
                CorrelationId = CorrelationId,
                InitiatingUserId = CallerUserId,
                InputParameters = new ParameterCollection
                {
                    { CrmSchema.Apis.Params.Target, new EntityReference(CrmSchema.Entities.Opportunity, BoundOppId) },
                    { CrmSchema.Apis.Params.Operation, "Submit" }
                }
            };
            var nestedExt13 = new FakePluginExecutionContext
            {
                MessageName = "Create",
                Mode = 0,
                IsInTransaction = true,
                Stage = 20,
                Depth = 2,
                CorrelationId = CorrelationId,
                InitiatingUserId = CallerUserId,
                UserId = WriterUserId,
                ParentContext = apiExt13
            };
            Assert.True(TrustedOperationScope.TryResolveFromParentChain(nestedExt13, out scope));
            Assert.Equal(CrmSchema.Apis.ManageDealApproval, scope.ApiMessage);
            Assert.Equal("Submit", scope.SubOperation);
        }

        [Fact]
        public void TrustedOperationScope_TryResolveFromParentChain_RejectsDepthAndSharedVariablesForgeryAndMismatches()
        {
            TrustedApiScope scope;

            // Depth > 1 without recognized API ancestor + forged SharedVariables tag -> MUST reject
            var rawUpdateParent = new FakePluginExecutionContext
            {
                MessageName = "Update",
                Mode = 0,
                IsInTransaction = true,
                Stage = 30,
                CorrelationId = CorrelationId,
                InitiatingUserId = CallerUserId
            };
            var forgedCtx = new FakePluginExecutionContext
            {
                MessageName = "Update",
                Mode = 0,
                IsInTransaction = true,
                Stage = 20,
                Depth = 3,
                CorrelationId = CorrelationId,
                InitiatingUserId = CallerUserId,
                ParentContext = rawUpdateParent,
                SharedVariables = new ParameterCollection
                {
                    { "TrustedOperation", "vn_TransitionOpportunity" },
                    { "BypassGuard", true }
                }
            };
            Assert.False(TrustedOperationScope.TryResolveFromParentChain(forgedCtx, out scope));
            Assert.Null(scope);

            // Ancestor Stage != 30 -> reject
            var wrongStageAncestor = new FakePluginExecutionContext
            {
                MessageName = CrmSchema.Apis.TransitionOpportunity,
                Mode = 0,
                IsInTransaction = true,
                Stage = 20,
                CorrelationId = CorrelationId,
                InitiatingUserId = CallerUserId,
                InputParameters = new ParameterCollection
                {
                    { CrmSchema.Apis.Params.Target, new EntityReference(CrmSchema.Entities.Opportunity, BoundOppId) }
                }
            };
            var nested = new FakePluginExecutionContext
            {
                MessageName = "Update",
                Mode = 0,
                IsInTransaction = true,
                Stage = 20,
                Depth = 2,
                CorrelationId = CorrelationId,
                InitiatingUserId = CallerUserId,
                ParentContext = wrongStageAncestor
            };
            Assert.False(TrustedOperationScope.TryResolveFromParentChain(nested, out scope));

            // Mismatched CorrelationId -> reject
            wrongStageAncestor.Stage = 30;
            wrongStageAncestor.CorrelationId = Guid.NewGuid();
            Assert.False(TrustedOperationScope.TryResolveFromParentChain(nested, out scope));

            // Mismatched InitiatingUserId -> reject
            wrongStageAncestor.CorrelationId = CorrelationId;
            wrongStageAncestor.InitiatingUserId = OtherUserId;
            Assert.False(TrustedOperationScope.TryResolveFromParentChain(nested, out scope));

            // Non-transactional ancestor -> reject
            wrongStageAncestor.InitiatingUserId = CallerUserId;
            wrongStageAncestor.IsInTransaction = false;
            Assert.False(TrustedOperationScope.TryResolveFromParentChain(nested, out scope));

            // Wrong bound entity type (Lead passed to TransitionOpportunity) -> reject
            wrongStageAncestor.IsInTransaction = true;
            wrongStageAncestor.InputParameters[CrmSchema.Apis.Params.Target] = new EntityReference(CrmSchema.Entities.Lead, BoundLeadId);
            Assert.False(TrustedOperationScope.TryResolveFromParentChain(nested, out scope));
        }

        [Fact]
        public void TrustedOperationScope_WriterIdentityAndAttributeAllowlist_EnforceStrictMatching()
        {
            var config = CrmPolicyConfig.CreateDefaults(BaseCurrencyId, WriterUserId);

            var writerCtx = new FakePluginExecutionContext
            {
                UserId = WriterUserId,
                InitiatingUserId = CallerUserId
            };
            var repCtx = new FakePluginExecutionContext
            {
                UserId = CallerUserId,
                InitiatingUserId = CallerUserId
            };

            Assert.True(TrustedOperationScope.IsExecutedByConfiguredInternalWriter(writerCtx, config));
            Assert.False(TrustedOperationScope.IsExecutedByConfiguredInternalWriter(repCtx, config));
            Assert.False(TrustedOperationScope.IsExecutedByConfiguredInternalWriter(writerCtx, CrmPolicyConfig.CreateDefaults(BaseCurrencyId, null)));

            var target = new Entity(CrmSchema.Entities.Opportunity, BoundOppId);
            target[CrmSchema.Opportunity.CommandSequence] = 2;
            target[CrmSchema.Opportunity.CurrentApproval] = null;

            var allowlist = new[]
            {
                CrmSchema.Opportunity.Id,
                CrmSchema.Opportunity.CommandSequence,
                CrmSchema.Opportunity.CurrentApproval
            };
            Assert.True(TrustedOperationScope.AreAllAttributesAllowed(target, allowlist));

            target[CrmSchema.Opportunity.Total] = new Money(99999m);
            Assert.False(TrustedOperationScope.AreAllAttributesAllowed(target, allowlist));
        }

        [Fact]
        public void AuthorizationService_CallerAndInternalWriterFactories_EnforceIdentityAndFailSafely()
        {
            var callerService = new FakeOrganizationService();
            var writerService = new FakeOrganizationService
            {
                OnRetrieve = (entityName, id, cols) =>
                {
                    var u = new Entity(CrmSchema.Entities.SystemUser, id);
                    u[CrmSchema.Platform.IsDisabled] = false;
                    u[CrmSchema.Platform.BusinessUnitId] = new EntityReference(CrmSchema.Entities.BusinessUnit, Guid.NewGuid());
                    return u;
                }
            };

            var factory = new FakeOrganizationServiceFactory(uid =>
                uid == CallerUserId ? callerService :
                uid == WriterUserId ? writerService : null);

            // CreateCallerService passes InitiatingUserId (not UserId, never null)
            var ctx = new FakePluginExecutionContext
            {
                InitiatingUserId = CallerUserId,
                UserId = WriterUserId,
                CorrelationId = CorrelationId
            };
            var resolvedCallerSvc = AuthorizationService.CreateCallerService(factory, ctx);
            Assert.Same(callerService, resolvedCallerSvc);
            Assert.Equal(CallerUserId, factory.RequestedUserIds[0]);

            // CreateInternalWriterService passes configured writer GUID (never null)
            var configured = CrmPolicyConfig.CreateDefaults(BaseCurrencyId, WriterUserId);
            var resolvedWriterSvc = AuthorizationService.CreateInternalWriterService(factory, configured, CorrelationId);
            Assert.Same(writerService, resolvedWriterSvc);
            Assert.Equal(WriterUserId, factory.RequestedUserIds[1]);
            Assert.DoesNotContain((Guid?)null, factory.RequestedUserIds);

            // Missing InternalWriterUserId -> CRM013
            var unconfigured = CrmPolicyConfig.CreateDefaults(BaseCurrencyId, null);
            AssertCrmError(
                CrmError.CRM013,
                () => AuthorizationService.CreateInternalWriterService(factory, unconfigured, CorrelationId));

            // Disabled writer user -> CRM013
            var disabledWriterSvc = new FakeOrganizationService
            {
                OnRetrieve = (entityName, id, cols) =>
                {
                    var u = new Entity(CrmSchema.Entities.SystemUser, id);
                    u[CrmSchema.Platform.IsDisabled] = true;
                    return u;
                }
            };
            var disabledFactory = new FakeOrganizationServiceFactory(uid => disabledWriterSvc);
            AssertCrmError(
                CrmError.CRM013,
                () => AuthorizationService.CreateInternalWriterService(disabledFactory, configured, CorrelationId));

            // Writer lookup fault -> CRM013
            var faultingWriterSvc = new FakeOrganizationService
            {
                OnRetrieve = (entityName, id, cols) => throw CreateFault(-2147220969, "systemuser not found")
            };
            var faultingFactory = new FakeOrganizationServiceFactory(uid => faultingWriterSvc);
            AssertCrmError(
                CrmError.CRM013,
                () => AuthorizationService.CreateInternalWriterService(faultingFactory, configured, CorrelationId));
        }

        [Fact]
        public void AuthorizationService_EnsureAccessRightsAndUserHasRole_EnforcePermissionsCleanly()
        {
            var target = new EntityReference(CrmSchema.Entities.Opportunity, BoundOppId);

            // Sufficient rights -> passes
            var allowService = new FakeOrganizationService
            {
                OnExecute = req =>
                {
                    Assert.IsType<RetrievePrincipalAccessRequest>(req);
                    var resp = new RetrievePrincipalAccessResponse();
                    resp.Results["AccessRights"] = AccessRights.ReadAccess | AccessRights.WriteAccess | AccessRights.AppendToAccess;
                    return resp;
                }
            };
            AuthorizationService.EnsureAccessRights(
                allowService,
                CallerUserId,
                target,
                AccessRights.ReadAccess | AccessRights.WriteAccess,
                CrmError.CRM013,
                "Caller lacks required Opportunity rights.",
                CorrelationId);

            // Missing WriteAccess -> throws specified error code
            var readOnlyService = new FakeOrganizationService
            {
                OnExecute = req =>
                {
                    var resp = new RetrievePrincipalAccessResponse();
                    resp.Results["AccessRights"] = AccessRights.ReadAccess;
                    return resp;
                }
            };
            AssertCrmError(
                CrmError.CRM013,
                () => AuthorizationService.EnsureAccessRights(
                    readOnlyService,
                    CallerUserId,
                    target,
                    AccessRights.ReadAccess | AccessRights.WriteAccess,
                    CrmError.CRM013,
                    "Caller lacks required Opportunity rights.",
                    CorrelationId));

            // Dataverse FaultException -> mapped cleanly to specified CrmError without leaking record existence
            var faultService = new FakeOrganizationService
            {
                OnExecute = req => throw CreateFault(-2147220969, "vn_opportunity With Id = ... Does Not Exist in Sales B")
            };
            var ex = Assert.Throws<InvalidPluginExecutionException>(() => AuthorizationService.EnsureAccessRights(
                faultService,
                CallerUserId,
                target,
                AccessRights.ReadAccess,
                CrmError.CRM013,
                "Opportunity is not accessible.",
                CorrelationId));
            Assert.StartsWith("[CRM013] Opportunity is not accessible.", ex.Message);
            Assert.DoesNotContain("Sales B", ex.Message);

            // UserHasRole -> true when role match exists, false when empty
            var hasRoleService = new FakeOrganizationService
            {
                OnRetrieveMultiple = q => new EntityCollection(new[] { new Entity(CrmSchema.Entities.Role, Guid.NewGuid()) })
            };
            Assert.True(AuthorizationService.UserHasRole(hasRoleService, CallerUserId, CrmSchema.Roles.SalesManager));

            var noRoleService = new FakeOrganizationService
            {
                OnRetrieveMultiple = q => new EntityCollection()
            };
            Assert.False(AuthorizationService.UserHasRole(noRoleService, CallerUserId, CrmSchema.Roles.SalesAdministrator));
        }
    }
}
