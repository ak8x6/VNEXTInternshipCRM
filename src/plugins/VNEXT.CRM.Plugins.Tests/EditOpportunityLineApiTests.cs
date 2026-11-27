using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using VNEXT.CRM.Plugins.Apis;
using VNEXT.CRM.Plugins.Core;
using Xunit;

namespace VNEXT.CRM.Plugins.Tests
{
    public class EditOpportunityLineApiTests
    {
        private static readonly Guid CorrelationId = new Guid("d1d1d1d1-2222-2222-2222-222222222222");
        private static readonly Guid BaseCurrencyId = new Guid("11111111-1111-1111-1111-111111111111");
        private static readonly Guid ForeignCurrencyId = new Guid("12121212-1212-1212-1212-121212121212");
        private static readonly Guid RepUserId = new Guid("22222222-2222-2222-2222-222222222222");
        private static readonly Guid ManagerUserId = new Guid("33333333-3333-3333-3333-333333333333");
        private static readonly Guid WriterUserId = new Guid("44444444-4444-4444-4444-444444444444");
        private static readonly Guid SalesABuId = new Guid("55555555-5555-5555-5555-555555555555");
        private static readonly Guid OppId = new Guid("66666666-6666-6666-6666-666666666666");
        private static readonly Guid OtherOppId = new Guid("67676767-6767-6767-6767-676767676767");
        private static readonly Guid ProductId = new Guid("77777777-7777-7777-7777-777777777777");
        private static readonly Guid OtherProductId = new Guid("78787878-7878-7878-7878-787878787878");
        private static readonly Guid Line1Id = new Guid("88888888-8888-8888-8888-888888888888");
        private static readonly Guid Line2Id = new Guid("89898989-8989-8989-8989-898989898989");
        private static readonly Guid ApprovalId = new Guid("99999999-9999-9999-9999-999999999999");

        private static void AssertCrmError(string expectedCode, Action action)
        {
            var ex = Assert.Throws<InvalidPluginExecutionException>(action);
            Assert.StartsWith("[" + expectedCode + "] ", ex.Message);
            Assert.Contains("(CorrelationId: " + CorrelationId.ToString("D") + ")", ex.Message);
        }

        private static FaultException<OrganizationServiceFault> CreateFault(int errorCode, string message)
        {
            return new FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault { ErrorCode = errorCode, Message = message },
                new FaultReason(message));
        }

        private static Entity BuildOpenOpportunity(
            int stage = CrmSchema.Opportunity.StageValues.Develop,
            Guid? currentApprovalId = null,
            int commandSequence = 2,
            string rowVersion = "3001")
        {
            var opp = new Entity(CrmSchema.Entities.Opportunity, OppId)
            {
                RowVersion = rowVersion
            };
            opp[CrmSchema.Opportunity.StateCode] = new OptionSetValue(CrmSchema.Opportunity.State.Active);
            opp[CrmSchema.Opportunity.StatusCode] = new OptionSetValue(CrmSchema.Opportunity.Status.Open);
            opp[CrmSchema.Opportunity.Stage] = new OptionSetValue(stage);
            opp[CrmSchema.Opportunity.CommandSequence] = commandSequence;
            opp[CrmSchema.Opportunity.Total] = new Money(0m);
            opp[CrmSchema.Opportunity.MaxDiscount] = 0m;
            opp[CrmSchema.Opportunity.OwnerId] = new EntityReference(CrmSchema.Entities.SystemUser, RepUserId);
            opp[CrmSchema.Opportunity.TransactionCurrencyId] = new EntityReference(CrmSchema.Entities.TransactionCurrency, BaseCurrencyId);

            if (currentApprovalId.HasValue)
            {
                opp[CrmSchema.Opportunity.CurrentApproval] = new EntityReference(CrmSchema.Entities.ApprovalRequest, currentApprovalId.Value);
            }

            return opp;
        }

        private static Entity BuildProduct(
            Guid id,
            string sku = "CRM-ENT-01",
            string name = "Enterprise License",
            decimal unitPrice = 100.0050m,
            bool active = true,
            Guid? currencyId = null)
        {
            var prod = new Entity(CrmSchema.Entities.Product, id);
            prod[CrmSchema.Product.StateCode] = new OptionSetValue(active ? CrmSchema.Product.State.Active : CrmSchema.Product.State.Inactive);
            prod[CrmSchema.Product.Sku] = sku;
            prod[CrmSchema.Product.Name] = name;
            prod[CrmSchema.Product.UnitPrice] = new Money(unitPrice);
            prod[CrmSchema.Product.TransactionCurrencyId] = new EntityReference(CrmSchema.Entities.TransactionCurrency, currencyId ?? BaseCurrencyId);
            return prod;
        }

        private static FakePluginExecutionContext BuildApiContext(
            Guid callerId,
            string operation,
            string rowVersion = "W/\"3001\"",
            Guid? lineId = null,
            Guid? productId = null,
            decimal? quantity = null,
            decimal? discountPercent = null,
            bool isInTransaction = true)
        {
            var inputs = new ParameterCollection
            {
                { CrmSchema.Apis.Params.Target, new EntityReference(CrmSchema.Entities.Opportunity, OppId) },
                { CrmSchema.Apis.Params.Operation, operation }
            };

            if (rowVersion != null) inputs[CrmSchema.Apis.Params.ExpectedRowVersion] = rowVersion;
            if (lineId.HasValue) inputs[CrmSchema.Apis.Params.LineId] = lineId.Value;
            if (productId.HasValue) inputs[CrmSchema.Apis.Params.ProductId] = productId.Value;
            if (quantity.HasValue) inputs[CrmSchema.Apis.Params.Quantity] = quantity.Value;
            if (discountPercent.HasValue) inputs[CrmSchema.Apis.Params.DiscountPercent] = discountPercent.Value;

            return new FakePluginExecutionContext
            {
                MessageName = CrmSchema.Apis.EditOpportunityLine,
                PrimaryEntityName = CrmSchema.Entities.Opportunity,
                PrimaryEntityId = OppId,
                Stage = 30,
                Mode = 0,
                IsInTransaction = isInTransaction,
                CorrelationId = CorrelationId,
                InitiatingUserId = callerId,
                UserId = callerId,
                InputParameters = inputs
            };
        }

        private static ApiFakeOrganizationService BuildStatefulCallerService(
            Entity opp,
            Dictionary<Guid, Entity> products,
            Dictionary<Guid, Entity> linesStore,
            bool configureWriter = true,
            AccessRights oppRights = AccessRights.ReadAccess | AccessRights.WriteAccess,
            Func<Entity, Guid> onCreateOverride = null,
            List<string> callLog = null)
        {
            return new ApiFakeOrganizationService
            {
                OnRetrieve = (entityName, id, cols) =>
                {
                    if (entityName == CrmSchema.Entities.Opportunity && id == OppId) return opp;
                    if (entityName == CrmSchema.Entities.Product && products != null && products.ContainsKey(id)) return products[id];
                    if (entityName == CrmSchema.Entities.OpportunityLine)
                    {
                        if (linesStore != null && linesStore.ContainsKey(id))
                        {
                            return linesStore[id];
                        }

                        throw CreateFault(-2147220969, "vn_opportunityline does not exist");
                    }

                    return null;
                },
                OnCreate = entity =>
                {
                    if (callLog != null) callLog.Add("Create:" + entity.LogicalName);
                    if (onCreateOverride != null) return onCreateOverride(entity);

                    var newId = entity.Id != Guid.Empty ? entity.Id : Guid.NewGuid();
                    entity.Id = newId;
                    entity[CrmSchema.OpportunityLine.StateCode] = new OptionSetValue(CrmSchema.OpportunityLine.State.Active);
                    if (entity.LogicalName == CrmSchema.Entities.OpportunityLine && linesStore != null)
                    {
                        linesStore[newId] = entity;
                    }

                    return newId;
                },
                OnUpdate = entity =>
                {
                    if (callLog != null) callLog.Add("Update:" + entity.LogicalName);
                },
                OnDelete = (entityName, id) =>
                {
                    if (callLog != null) callLog.Add("Delete:" + entityName);
                    if (entityName == CrmSchema.Entities.OpportunityLine && linesStore != null)
                    {
                        linesStore.Remove(id);
                    }
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

                        if (qe.EntityName == CrmSchema.Entities.OpportunityLine && linesStore != null)
                        {
                            var activeForOpp = linesStore.Values
                                .Where(l => l.GetAttributeValue<EntityReference>(CrmSchema.OpportunityLine.Opportunity)?.Id == OppId
                                    && (l.GetAttributeValue<OptionSetValue>(CrmSchema.OpportunityLine.StateCode)?.Value ?? 0) == 0)
                                .ToList();
                            return new EntityCollection(activeForOpp);
                        }
                    }

                    return new EntityCollection();
                }
            };
        }

        private static ApiFakeOrganizationService BuildWriterService(Entity approvalRequest = null)
        {
            return new ApiFakeOrganizationService
            {
                OnRetrieve = (entityName, id, cols) =>
                {
                    if (entityName == CrmSchema.Entities.SystemUser && id == WriterUserId)
                    {
                        var u = new Entity(CrmSchema.Entities.SystemUser, WriterUserId);
                        u[CrmSchema.Platform.IsDisabled] = false;
                        u[CrmSchema.Platform.BusinessUnitId] = new EntityReference(CrmSchema.Entities.BusinessUnit, SalesABuId);
                        return u;
                    }

                    if (entityName == CrmSchema.Entities.ApprovalRequest && approvalRequest != null && id == approvalRequest.Id)
                    {
                        return approvalRequest;
                    }

                    return null;
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
                uid == ctx.InitiatingUserId ? callerService :
                uid == WriterUserId ? effectiveWriter : null);

            new EditOpportunityLineApiPlugin().Execute(new FakeServiceProvider(ctx, factory));
        }

        [Fact]
        public void Add_CanonicalPricingSnapshotImmutabilityAndManagerOwnerTransfer_SucceedExact()
        {
            var callLog = new List<string>();
            var opp = BuildOpenOpportunity(commandSequence: 2);
            var product = BuildProduct(ProductId, sku: "CRM-ENT-01", name: new string('A', 120), unitPrice: 100.0050m);
            var products = new Dictionary<Guid, Entity> { { ProductId, product } };
            var linesStore = new Dictionary<Guid, Entity>();

            // Manager adds canonical Line (3 x 100.0050 @ 10% = 300.02 / 30.00 / 270.02) on Rep's Opportunity
            var caller = BuildStatefulCallerService(opp, products, linesStore, callLog: callLog);
            var writer = BuildWriterService();
            var ctx = BuildApiContext(ManagerUserId, "Add", lineId: Line1Id, productId: ProductId, quantity: 3m, discountPercent: 10m);

            ExecuteApi(ctx, caller, writer);

            // Latch MUST execute FIRST before Line Create
            Assert.Equal("Latch:BumpCommandSequence", callLog[0]);
            var latchReq = caller.ExecutedRequests.OfType<UpdateRequest>().Single();
            Assert.Equal(ConcurrencyBehavior.IfRowVersionMatches, latchReq.ConcurrencyBehavior);
            Assert.Equal("3001", latchReq.Target.RowVersion);
            Assert.Equal(3, latchReq.Target.GetAttributeValue<int>(CrmSchema.Opportunity.CommandSequence));

            // Line created with exact canonical snapshot & <= 100-char vn_name
            Assert.Single(caller.CreatedEntities);
            var createdLine = caller.CreatedEntities[0];
            Assert.Equal(Line1Id, createdLine.Id);
            Assert.True(createdLine.GetAttributeValue<string>(CrmSchema.OpportunityLine.Name).Length <= 100);
            Assert.Equal("CRM-ENT-01", createdLine.GetAttributeValue<string>(CrmSchema.OpportunityLine.SnapshotSku));
            Assert.Equal(100.0050m, createdLine.GetAttributeValue<Money>(CrmSchema.OpportunityLine.UnitPrice).Value);
            Assert.Equal(300.02m, createdLine.GetAttributeValue<Money>(CrmSchema.OpportunityLine.GrossAmount).Value);
            Assert.Equal(30.00m, createdLine.GetAttributeValue<Money>(CrmSchema.OpportunityLine.DiscountAmount).Value);
            Assert.Equal(270.02m, createdLine.GetAttributeValue<Money>(CrmSchema.OpportunityLine.NetAmount).Value);

            // Updates: 1) Line owner transfer to Rep, 2) Opportunity totals + lastprogresson
            Assert.Equal(2, caller.UpdatedEntities.Count);
            Assert.Equal(CrmSchema.Entities.OpportunityLine, caller.UpdatedEntities[0].LogicalName);
            Assert.Equal(RepUserId, caller.UpdatedEntities[0].GetAttributeValue<EntityReference>(CrmSchema.OpportunityLine.OwnerId).Id);

            var oppTotalsPatch = caller.UpdatedEntities[1];
            Assert.Equal(CrmSchema.Entities.Opportunity, oppTotalsPatch.LogicalName);
            Assert.Equal(270.02m, oppTotalsPatch.GetAttributeValue<Money>(CrmSchema.Opportunity.Total).Value);
            Assert.Equal(10m, oppTotalsPatch.GetAttributeValue<decimal>(CrmSchema.Opportunity.MaxDiscount));
            Assert.True(oppTotalsPatch.Contains(CrmSchema.Opportunity.LastProgressOn));

            // Zero approval writes when vn_currentapproval is null
            Assert.Empty(writer.UpdatedEntities);

            // Exact 4 §7.3 OutputParameters (NO CommandSequence)
            Assert.Equal(4, ctx.OutputParameters.Count);
            Assert.False(ctx.OutputParameters.Contains(CrmSchema.Apis.Params.CommandSequence));
            Assert.Equal(Line1Id, (Guid)ctx.OutputParameters[CrmSchema.Apis.Params.LineId]);
            Assert.Equal(270.02m, (decimal)ctx.OutputParameters[CrmSchema.Apis.Params.Total]);
            Assert.False((bool)ctx.OutputParameters[CrmSchema.Apis.Params.WasAlreadyApplied]);
            Assert.Equal(CorrelationId.ToString("D"), (string)ctx.OutputParameters[CrmSchema.Apis.Params.CorrelationId]);

            // Later Product catalog price change does NOT reprice Line 1 when Line 2 is added
            product[CrmSchema.Product.UnitPrice] = new Money(200.0000m);
            var ctx2 = BuildApiContext(RepUserId, "Add", lineId: Line2Id, productId: ProductId, quantity: 1m, discountPercent: 0m);
            ExecuteApi(ctx2, caller, writer);
            Assert.Equal(470.02m, (decimal)ctx2.OutputParameters[CrmSchema.Apis.Params.Total]); // 270.02 (frozen) + 200.00
        }

        [Fact]
        public void Add_IdempotentRetry_ReturnsWasAlreadyAppliedTrueWithZeroWritesAndRejectsMismatches()
        {
            var opp = BuildOpenOpportunity(currentApprovalId: ApprovalId, commandSequence: 5, rowVersion: "5001");
            var existingLine = new Entity(CrmSchema.Entities.OpportunityLine, Line1Id);
            existingLine[CrmSchema.OpportunityLine.Opportunity] = new EntityReference(CrmSchema.Entities.Opportunity, OppId);
            existingLine[CrmSchema.OpportunityLine.Product] = new EntityReference(CrmSchema.Entities.Product, ProductId);
            existingLine[CrmSchema.OpportunityLine.Quantity] = 3m;
            existingLine[CrmSchema.OpportunityLine.DiscountPercent] = 10m;
            existingLine[CrmSchema.OpportunityLine.NetAmount] = new Money(270.02m);
            existingLine[CrmSchema.OpportunityLine.StateCode] = new OptionSetValue(CrmSchema.OpportunityLine.State.Active);

            var linesStore = new Dictionary<Guid, Entity> { { Line1Id, existingLine } };
            var products = new Dictionary<Guid, Entity> { { ProductId, BuildProduct(ProductId) } };
            var caller = BuildStatefulCallerService(opp, products, linesStore);
            var writer = BuildWriterService();

            // Retry with STALE (non-blank) RowVersion & matching LineId/Product/Qty/Discount -> WasAlreadyApplied=true, ZERO writes
            var retryCtx = BuildApiContext(RepUserId, "Add", rowVersion: "W/\"4000\"", lineId: Line1Id, productId: ProductId, quantity: 3m, discountPercent: 10m);
            ExecuteApi(retryCtx, caller, writer);

            Assert.Empty(caller.CreatedEntities);
            Assert.Empty(caller.UpdatedEntities);
            Assert.DoesNotContain(caller.ExecutedRequests, r => r is UpdateRequest);
            Assert.Empty(writer.UpdatedEntities);
            Assert.Equal(4, retryCtx.OutputParameters.Count);
            Assert.False(retryCtx.OutputParameters.Contains(CrmSchema.Apis.Params.CommandSequence));
            Assert.Equal(Line1Id, (Guid)retryCtx.OutputParameters[CrmSchema.Apis.Params.LineId]);
            Assert.Equal(270.02m, (decimal)retryCtx.OutputParameters[CrmSchema.Apis.Params.Total]);
            Assert.True((bool)retryCtx.OutputParameters[CrmSchema.Apis.Params.WasAlreadyApplied]);
            Assert.Equal(CorrelationId.ToString("D"), (string)retryCtx.OutputParameters[CrmSchema.Apis.Params.CorrelationId]);

            // Retry same LineId with different Product / Quantity / Discount -> CRM010
            AssertCrmError(CrmError.CRM010, () => ExecuteApi(
                BuildApiContext(RepUserId, "Add", lineId: Line1Id, productId: OtherProductId, quantity: 3m, discountPercent: 10m),
                caller, writer));
            AssertCrmError(CrmError.CRM010, () => ExecuteApi(
                BuildApiContext(RepUserId, "Add", lineId: Line1Id, productId: ProductId, quantity: 4m, discountPercent: 10m),
                caller, writer));
            AssertCrmError(CrmError.CRM010, () => ExecuteApi(
                BuildApiContext(RepUserId, "Add", lineId: Line1Id, productId: ProductId, quantity: 3m, discountPercent: 5m),
                caller, writer));

            // Retry same LineId belonging to a DIFFERENT Opportunity -> CRM010
            existingLine[CrmSchema.OpportunityLine.Opportunity] = new EntityReference(CrmSchema.Entities.Opportunity, OtherOppId);
            AssertCrmError(CrmError.CRM010, () => ExecuteApi(
                BuildApiContext(RepUserId, "Add", lineId: Line1Id, productId: ProductId, quantity: 3m, discountPercent: 10m),
                caller, writer));

            // Blank ExpectedRowVersion still fails CRM021
            AssertCrmError(CrmError.CRM021, () => ExecuteApi(
                BuildApiContext(RepUserId, "Add", rowVersion: "  ", lineId: Line1Id, productId: ProductId, quantity: 3m, discountPercent: 10m),
                caller, writer));
        }

        [Fact]
        public void Remove_RecalculatesTotalsEnforcesProposeCloseLastLineGuardAndRejectsInvalidTargets()
        {
            // Removing the last line in Develop -> Total = 0.00, MaxDiscount = 0.00
            var oppDevelop = BuildOpenOpportunity(stage: CrmSchema.Opportunity.StageValues.Develop);
            var line1 = new Entity(CrmSchema.Entities.OpportunityLine, Line1Id);
            line1[CrmSchema.OpportunityLine.Opportunity] = new EntityReference(CrmSchema.Entities.Opportunity, OppId);
            line1[CrmSchema.OpportunityLine.StateCode] = new OptionSetValue(CrmSchema.OpportunityLine.State.Active);
            line1[CrmSchema.OpportunityLine.NetAmount] = new Money(500m);
            line1[CrmSchema.OpportunityLine.DiscountPercent] = 8m;

            var linesDevelop = new Dictionary<Guid, Entity> { { Line1Id, line1 } };
            var callerDevelop = BuildStatefulCallerService(oppDevelop, null, linesDevelop);
            var ctxRemove = BuildApiContext(RepUserId, "Remove", lineId: Line1Id);

            ExecuteApi(ctxRemove, callerDevelop, BuildWriterService());
            Assert.Empty(linesDevelop);
            Assert.Equal(0.00m, callerDevelop.UpdatedEntities[0].GetAttributeValue<Money>(CrmSchema.Opportunity.Total).Value);
            Assert.Equal(0.00m, callerDevelop.UpdatedEntities[0].GetAttributeValue<decimal>(CrmSchema.Opportunity.MaxDiscount));
            Assert.Equal(4, ctxRemove.OutputParameters.Count);
            Assert.False(ctxRemove.OutputParameters.Contains(CrmSchema.Apis.Params.CommandSequence));
            Assert.Equal(Line1Id, (Guid)ctxRemove.OutputParameters[CrmSchema.Apis.Params.LineId]);
            Assert.Equal(0.00m, (decimal)ctxRemove.OutputParameters[CrmSchema.Apis.Params.Total]);
            Assert.False((bool)ctxRemove.OutputParameters[CrmSchema.Apis.Params.WasAlreadyApplied]);
            Assert.Equal(CorrelationId.ToString("D"), (string)ctxRemove.OutputParameters[CrmSchema.Apis.Params.CorrelationId]);

            // Removing the last line in Propose or Close -> CRM007
            foreach (var stage in new[] { CrmSchema.Opportunity.StageValues.Propose, CrmSchema.Opportunity.StageValues.Close })
            {
                var oppLater = BuildOpenOpportunity(stage: stage);
                var linesLater = new Dictionary<Guid, Entity> { { Line1Id, line1 } };
                var callerLater = BuildStatefulCallerService(oppLater, null, linesLater);
                AssertCrmError(CrmError.CRM007, () => ExecuteApi(
                    BuildApiContext(RepUserId, "Remove", lineId: Line1Id),
                    callerLater, BuildWriterService()));
            }

            // Missing Line -> CRM010
            AssertCrmError(CrmError.CRM010, () => ExecuteApi(
                BuildApiContext(RepUserId, "Remove", lineId: Guid.NewGuid()),
                BuildStatefulCallerService(oppDevelop, null, new Dictionary<Guid, Entity>())));

            // Wrong-Opportunity Line -> CRM010
            var foreignLine = new Entity(CrmSchema.Entities.OpportunityLine, Line2Id);
            foreignLine[CrmSchema.OpportunityLine.Opportunity] = new EntityReference(CrmSchema.Entities.Opportunity, OtherOppId);
            foreignLine[CrmSchema.OpportunityLine.StateCode] = new OptionSetValue(CrmSchema.OpportunityLine.State.Active);
            AssertCrmError(CrmError.CRM010, () => ExecuteApi(
                BuildApiContext(RepUserId, "Remove", lineId: Line2Id),
                BuildStatefulCallerService(oppDevelop, null, new Dictionary<Guid, Entity> { { Line2Id, foreignLine } })));
        }

        [Fact]
        public void ApprovalInvalidation_SupersedesPendingAndApproved_PreservesImmutableRejectedAndClearsPointer()
        {
            var products = new Dictionary<Guid, Entity> { { ProductId, BuildProduct(ProductId, unitPrice: 100m) } };

            // Pending & Approved -> mark Superseded (100000003) AND clear vn_currentapproval = null
            foreach (var resultVal in new[]
            {
                CrmSchema.ApprovalRequest.ResultValues.Pending,
                CrmSchema.ApprovalRequest.ResultValues.Approved
            })
            {
                var opp = BuildOpenOpportunity(currentApprovalId: ApprovalId);
                var req = new Entity(CrmSchema.Entities.ApprovalRequest, ApprovalId);
                req[CrmSchema.ApprovalRequest.Opportunity] = new EntityReference(CrmSchema.Entities.Opportunity, OppId);
                req[CrmSchema.ApprovalRequest.Result] = new OptionSetValue(resultVal);

                var caller = BuildStatefulCallerService(opp, products, new Dictionary<Guid, Entity>());
                var writer = BuildWriterService(req);

                ExecuteApi(BuildApiContext(RepUserId, "Add", lineId: Line1Id, productId: ProductId, quantity: 1m, discountPercent: 0m), caller, writer);

                Assert.Equal(2, writer.UpdatedEntities.Count);
                Assert.Equal(CrmSchema.Entities.ApprovalRequest, writer.UpdatedEntities[0].LogicalName);
                Assert.Equal(CrmSchema.ApprovalRequest.ResultValues.Superseded, writer.UpdatedEntities[0].GetAttributeValue<OptionSetValue>(CrmSchema.ApprovalRequest.Result).Value);
                Assert.Equal(CrmSchema.Entities.Opportunity, writer.UpdatedEntities[1].LogicalName);
                Assert.Null(writer.UpdatedEntities[1][CrmSchema.Opportunity.CurrentApproval]);
            }

            // Rejected -> Request UNTOUCHED, ONLY clears vn_currentapproval = null (BR19 & R-02)
            var oppRej = BuildOpenOpportunity(currentApprovalId: ApprovalId);
            var reqRej = new Entity(CrmSchema.Entities.ApprovalRequest, ApprovalId);
            reqRej[CrmSchema.ApprovalRequest.Opportunity] = new EntityReference(CrmSchema.Entities.Opportunity, OppId);
            reqRej[CrmSchema.ApprovalRequest.Result] = new OptionSetValue(CrmSchema.ApprovalRequest.ResultValues.Rejected);

            var existingLine = new Entity(CrmSchema.Entities.OpportunityLine, Line1Id);
            existingLine[CrmSchema.OpportunityLine.Opportunity] = new EntityReference(CrmSchema.Entities.Opportunity, OppId);
            existingLine[CrmSchema.OpportunityLine.StateCode] = new OptionSetValue(CrmSchema.OpportunityLine.State.Active);
            existingLine[CrmSchema.OpportunityLine.NetAmount] = new Money(100m);
            existingLine[CrmSchema.OpportunityLine.DiscountPercent] = 0m;

            var callerRej = BuildStatefulCallerService(oppRej, products, new Dictionary<Guid, Entity> { { Line1Id, existingLine } });
            var writerRej = BuildWriterService(reqRej);

            ExecuteApi(BuildApiContext(RepUserId, "Remove", lineId: Line1Id), callerRej, writerRej);

            Assert.Single(writerRej.UpdatedEntities);
            Assert.Equal(CrmSchema.Entities.Opportunity, writerRej.UpdatedEntities[0].LogicalName);
            Assert.True(writerRej.UpdatedEntities[0].Contains(CrmSchema.Opportunity.CurrentApproval));
            Assert.Null(writerRej.UpdatedEntities[0][CrmSchema.Opportunity.CurrentApproval]);
        }

        [Fact]
        public void EditOpportunityLine_NegativePaths_EnforceExactErrorCodes()
        {
            var opp = BuildOpenOpportunity();
            var activeProd = BuildProduct(ProductId, unitPrice: 100m);
            var inactiveProd = BuildProduct(OtherProductId, unitPrice: 100m, active: false);
            var foreignProd = BuildProduct(Guid.NewGuid(), unitPrice: 100m, currencyId: ForeignCurrencyId);
            var products = new Dictionary<Guid, Entity>
            {
                { ProductId, activeProd },
                { OtherProductId, inactiveProd },
                { foreignProd.Id, foreignProd }
            };

            // Invalid Operation -> CRM010
            AssertCrmError(CrmError.CRM010, () => ExecuteApi(BuildApiContext(RepUserId, "Update"), BuildStatefulCallerService(opp, products, new Dictionary<Guid, Entity>())));
            AssertCrmError(CrmError.CRM010, () => ExecuteApi(BuildApiContext(RepUserId, " "), BuildStatefulCallerService(opp, products, new Dictionary<Guid, Entity>())));

            // Inactive Product, foreign currency, out-of-range Discount (> 30%) / Quantity -> CRM008
            AssertCrmError(CrmError.CRM008, () => ExecuteApi(BuildApiContext(RepUserId, "Add", lineId: Line1Id, productId: OtherProductId, quantity: 1m, discountPercent: 0m), BuildStatefulCallerService(opp, products, new Dictionary<Guid, Entity>())));
            AssertCrmError(CrmError.CRM008, () => ExecuteApi(BuildApiContext(RepUserId, "Add", lineId: Line1Id, productId: foreignProd.Id, quantity: 1m, discountPercent: 0m), BuildStatefulCallerService(opp, products, new Dictionary<Guid, Entity>())));
            AssertCrmError(CrmError.CRM008, () => ExecuteApi(BuildApiContext(RepUserId, "Add", lineId: Line1Id, productId: ProductId, quantity: 1m, discountPercent: 30.01m), BuildStatefulCallerService(opp, products, new Dictionary<Guid, Entity>())));
            AssertCrmError(CrmError.CRM008, () => ExecuteApi(BuildApiContext(RepUserId, "Add", lineId: Line1Id, productId: ProductId, quantity: 0m, discountPercent: 0m), BuildStatefulCallerService(opp, products, new Dictionary<Guid, Entity>())));

            // Aggregate Total > 1,000,000,000 -> CRM009
            var bigExistingLine = new Entity(CrmSchema.Entities.OpportunityLine, Line2Id);
            bigExistingLine[CrmSchema.OpportunityLine.Opportunity] = new EntityReference(CrmSchema.Entities.Opportunity, OppId);
            bigExistingLine[CrmSchema.OpportunityLine.StateCode] = new OptionSetValue(CrmSchema.OpportunityLine.State.Active);
            bigExistingLine[CrmSchema.OpportunityLine.NetAmount] = new Money(999_999_950m);
            bigExistingLine[CrmSchema.OpportunityLine.DiscountPercent] = 0m;
            AssertCrmError(CrmError.CRM009, () => ExecuteApi(
                BuildApiContext(RepUserId, "Add", lineId: Line1Id, productId: ProductId, quantity: 1m, discountPercent: 0m),
                BuildStatefulCallerService(opp, products, new Dictionary<Guid, Entity> { { Line2Id, bigExistingLine } })));

            // Stale / missing ExpectedRowVersion and duplicate-ID Create fault (-2147220937) -> CRM021
            AssertCrmError(CrmError.CRM021, () => ExecuteApi(BuildApiContext(RepUserId, "Add", rowVersion: null, lineId: Line1Id, productId: ProductId, quantity: 1m), BuildStatefulCallerService(opp, products, new Dictionary<Guid, Entity>())));
            AssertCrmError(CrmError.CRM021, () => ExecuteApi(BuildApiContext(RepUserId, "Add", rowVersion: "W/\"9999\"", lineId: Line1Id, productId: ProductId, quantity: 1m), BuildStatefulCallerService(opp, products, new Dictionary<Guid, Entity>())));

            var dupCreateCaller = BuildStatefulCallerService(
                opp,
                products,
                new Dictionary<Guid, Entity>(),
                onCreateOverride: e => throw CreateFault(-2147220937, "Duplicate primary key"));
            AssertCrmError(CrmError.CRM021, () => ExecuteApi(
                BuildApiContext(RepUserId, "Add", lineId: Line1Id, productId: ProductId, quantity: 1m, discountPercent: 0m),
                dupCreateCaller));

            // Closed Opportunity -> CRM006
            var closedOpp = BuildOpenOpportunity();
            closedOpp[CrmSchema.Opportunity.StateCode] = new OptionSetValue(CrmSchema.Opportunity.State.Inactive);
            closedOpp[CrmSchema.Opportunity.StatusCode] = new OptionSetValue(CrmSchema.Opportunity.Status.Won);
            AssertCrmError(CrmError.CRM006, () => ExecuteApi(BuildApiContext(RepUserId, "Add", lineId: Line1Id, productId: ProductId, quantity: 1m), BuildStatefulCallerService(closedOpp, products, new Dictionary<Guid, Entity>())));

            // Caller lacking Write -> CRM013
            AssertCrmError(CrmError.CRM013, () => ExecuteApi(
                BuildApiContext(RepUserId, "Add", lineId: Line1Id, productId: ProductId, quantity: 1m),
                BuildStatefulCallerService(opp, products, new Dictionary<Guid, Entity>(), oppRights: AccessRights.ReadAccess)));

            // Unconfigured Internal Writer -> CRM013 with ZERO writes
            var unconfiguredCaller = BuildStatefulCallerService(opp, products, new Dictionary<Guid, Entity>(), configureWriter: false);
            AssertCrmError(CrmError.CRM013, () => ExecuteApi(BuildApiContext(RepUserId, "Add", lineId: Line1Id, productId: ProductId, quantity: 1m), unconfiguredCaller));
            Assert.Empty(unconfiguredCaller.CreatedEntities);
            Assert.Empty(unconfiguredCaller.UpdatedEntities);
            Assert.DoesNotContain(unconfiguredCaller.ExecutedRequests, r => r is UpdateRequest);

            // Non-transactional context -> CRM006
            AssertCrmError(CrmError.CRM006, () => ExecuteApi(
                BuildApiContext(RepUserId, "Add", lineId: Line1Id, productId: ProductId, quantity: 1m, isInTransaction: false),
                BuildStatefulCallerService(opp, products, new Dictionary<Guid, Entity>())));

            // §7.3 / BR15: Omitted LineId OR Guid.Empty on Add -> MUST throw CRM010 with ZERO writes
            var omittedCaller = BuildStatefulCallerService(BuildOpenOpportunity(), products, new Dictionary<Guid, Entity>());
            AssertCrmError(CrmError.CRM010, () => ExecuteApi(
                BuildApiContext(RepUserId, "Add", lineId: null, productId: ProductId, quantity: 2m, discountPercent: 0m),
                omittedCaller));
            Assert.Empty(omittedCaller.CreatedEntities);
            Assert.Empty(omittedCaller.UpdatedEntities);
            Assert.DoesNotContain(omittedCaller.ExecutedRequests, r => r is UpdateRequest);

            var emptyGuidCaller = BuildStatefulCallerService(BuildOpenOpportunity(), products, new Dictionary<Guid, Entity>());
            AssertCrmError(CrmError.CRM010, () => ExecuteApi(
                BuildApiContext(RepUserId, "Add", lineId: Guid.Empty, productId: ProductId, quantity: 2m, discountPercent: 0m),
                emptyGuidCaller));
            Assert.Empty(emptyGuidCaller.CreatedEntities);
            Assert.Empty(emptyGuidCaller.UpdatedEntities);
            Assert.DoesNotContain(emptyGuidCaller.ExecutedRequests, r => r is UpdateRequest);
        }
    }
}
