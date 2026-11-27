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
    internal sealed class ApiFakeOrganizationService : IOrganizationService
    {
        public Func<Entity, Guid> OnCreate { get; set; }
        public Func<string, Guid, ColumnSet, Entity> OnRetrieve { get; set; }
        public Action<Entity> OnUpdate { get; set; }
        public Action<string, Guid> OnDelete { get; set; }
        public Func<OrganizationRequest, OrganizationResponse> OnExecute { get; set; }
        public Func<QueryBase, EntityCollection> OnRetrieveMultiple { get; set; }

        public List<Entity> CreatedEntities { get; } = new List<Entity>();
        public List<Entity> UpdatedEntities { get; } = new List<Entity>();
        public List<OrganizationRequest> ExecutedRequests { get; } = new List<OrganizationRequest>();

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
            if (OnDelete != null)
            {
                OnDelete(entityName, id);
            }
        }

        public OrganizationResponse Execute(OrganizationRequest request)
        {
            ExecutedRequests.Add(request);
            return OnExecute != null ? OnExecute(request) : new OrganizationResponse();
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

    public class QualifyLeadApiTests
    {
        private static readonly Guid CorrelationId = new Guid("f1f1f1f1-f1f1-f1f1-f1f1-f1f1f1f1f1f1");
        private static readonly Guid BaseCurrencyId = new Guid("11111111-1111-1111-1111-111111111111");
        private static readonly Guid RepUserId = new Guid("22222222-2222-2222-2222-222222222222");
        private static readonly Guid ManagerUserId = new Guid("33333333-3333-3333-3333-333333333333");
        private static readonly Guid WriterUserId = new Guid("44444444-4444-4444-4444-444444444444");
        private static readonly Guid LeadId = new Guid("55555555-5555-5555-5555-555555555555");
        private static readonly Guid ExistingAccountId = new Guid("66666666-6666-6666-6666-666666666666");
        private static readonly Guid OtherAccountId = new Guid("67676767-6767-6767-6767-676767676767");
        private static readonly Guid ExistingContactId = new Guid("77777777-7777-7777-7777-777777777777");
        private static readonly Guid CreatedAccountId = new Guid("88888888-8888-8888-8888-888888888888");
        private static readonly Guid CreatedContactId = new Guid("99999999-9999-9999-9999-999999999999");
        private static readonly Guid CreatedOpportunityId = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        private static readonly Guid CreatedBpfId = new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        private static readonly Guid SalesABuId = new Guid("cccccccc-cccc-cccc-cccc-cccccccccccc");

        private static void AssertCrmError(string expectedCode, Action action)
        {
            var ex = Assert.Throws<InvalidPluginExecutionException>(action);
            Assert.StartsWith("[" + expectedCode + "] ", ex.Message);
            Assert.Contains("(CorrelationId: " + CorrelationId.ToString("D") + ")", ex.Message);
        }

        private static Entity BuildReadyLead(Guid ownerId, string rowVersion = "1001")
        {
            var lead = new Entity(CrmSchema.Entities.Lead, LeadId)
            {
                RowVersion = rowVersion
            };
            lead[CrmSchema.Lead.StateCode] = new OptionSetValue(CrmSchema.Lead.State.Active);
            lead[CrmSchema.Lead.StatusCode] = new OptionSetValue(CrmSchema.Lead.Status.Contacted);
            lead[CrmSchema.Lead.OwnerId] = new EntityReference(CrmSchema.Entities.SystemUser, ownerId);
            lead[CrmSchema.Lead.Name] = "Acme CRM Transformation";
            lead[CrmSchema.Lead.FirstName] = "Layla";
            lead[CrmSchema.Lead.LastName] = "Al-Mansoor";
            lead[CrmSchema.Lead.CompanyName] = "Acme Industrial";
            lead[CrmSchema.Lead.Email] = "layla@acme.example";
            lead[CrmSchema.Lead.Phone] = "+1-555-0199";
            lead[CrmSchema.Lead.Need] = "Replace spreadsheets with governed pipeline.";
            lead[CrmSchema.Lead.NextStep] = "Run scoping workshop";
            lead[CrmSchema.Lead.NextFollowUpDate] = DateTime.UtcNow.Date.AddDays(2);
            lead[CrmSchema.Lead.Budget] = new Money(45000m);
            lead[CrmSchema.Lead.BuyingTargetDate] = DateTime.UtcNow.Date.AddDays(45);
            lead[CrmSchema.Lead.TransactionCurrencyId] = new EntityReference(CrmSchema.Entities.TransactionCurrency, BaseCurrencyId);
            return lead;
        }

        private static FakePluginExecutionContext BuildApiContext(
            Guid callerId,
            string rowVersion = "W/\"1001\"",
            Guid? existingAccountId = null,
            Guid? existingContactId = null,
            string oppName = "Acme CRM Rollout",
            DateTime? expectedClose = null,
            bool isInTransaction = true,
            int mode = 0)
        {
            var inputs = new ParameterCollection
            {
                { CrmSchema.Apis.Params.Target, new EntityReference(CrmSchema.Entities.Lead, LeadId) },
                { CrmSchema.Apis.Params.OpportunityName, oppName },
                { CrmSchema.Apis.Params.ExpectedCloseDate, expectedClose ?? DateTime.UtcNow.Date.AddDays(30) }
            };

            if (rowVersion != null)
            {
                inputs[CrmSchema.Apis.Params.ExpectedRowVersion] = rowVersion;
            }

            if (existingAccountId.HasValue)
            {
                inputs[CrmSchema.Apis.Params.ExistingAccountId] = existingAccountId.Value;
            }

            if (existingContactId.HasValue)
            {
                inputs[CrmSchema.Apis.Params.ExistingContactId] = existingContactId.Value;
            }

            return new FakePluginExecutionContext
            {
                MessageName = CrmSchema.Apis.QualifyLead,
                PrimaryEntityName = CrmSchema.Entities.Lead,
                PrimaryEntityId = LeadId,
                Stage = 30,
                Mode = mode,
                IsInTransaction = isInTransaction,
                CorrelationId = CorrelationId,
                InitiatingUserId = callerId,
                UserId = callerId,
                InputParameters = inputs
            };
        }

        private static ApiFakeOrganizationService BuildCallerService(
            Entity lead,
            bool configureWriter = true,
            Entity existingAccount = null,
            Entity existingContact = null,
            Func<Entity, Guid> onCreateOverride = null,
            Func<OrganizationRequest, OrganizationResponse> onExecuteOverride = null,
            HashSet<Guid> deniedAccessIds = null)
        {
            return new ApiFakeOrganizationService
            {
                OnRetrieve = (entityName, id, cols) =>
                {
                    if (entityName == CrmSchema.Entities.Lead && id == LeadId) return lead;
                    if (entityName == CrmSchema.Entities.Account && existingAccount != null && id == existingAccount.Id) return existingAccount;
                    if (entityName == CrmSchema.Entities.Contact && existingContact != null && id == existingContact.Id) return existingContact;
                    return null;
                },
                OnCreate = entity =>
                {
                    if (onCreateOverride != null)
                    {
                        return onCreateOverride(entity);
                    }

                    if (entity.LogicalName == CrmSchema.Entities.Account) return CreatedAccountId;
                    if (entity.LogicalName == CrmSchema.Entities.Contact) return CreatedContactId;
                    if (entity.LogicalName == CrmSchema.Entities.Opportunity) return CreatedOpportunityId;
                    return Guid.NewGuid();
                },
                OnExecute = req =>
                {
                    if (onExecuteOverride != null)
                    {
                        return onExecuteOverride(req);
                    }

                    var accessReq = req as RetrievePrincipalAccessRequest;
                    if (accessReq != null)
                    {
                        var rights = deniedAccessIds != null && deniedAccessIds.Contains(accessReq.Target.Id)
                            ? AccessRights.None
                            : AccessRights.ReadAccess | AccessRights.WriteAccess | AccessRights.AppendToAccess;
                        var resp = new RetrievePrincipalAccessResponse();
                        resp.Results["AccessRights"] = rights;
                        return resp;
                    }

                    if (req is UpdateRequest)
                    {
                        return new UpdateResponse();
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
                            if (!configureWriter)
                            {
                                return new EntityCollection();
                            }

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

        private static ApiFakeOrganizationService BuildWriterService()
        {
            return new ApiFakeOrganizationService
            {
                OnRetrieve = (entityName, id, cols) =>
                {
                    if (entityName == CrmSchema.Entities.SystemUser)
                    {
                        var user = new Entity(CrmSchema.Entities.SystemUser, id);
                        user[CrmSchema.Platform.IsDisabled] = false;
                        user[CrmSchema.Platform.BusinessUnitId] = new EntityReference(CrmSchema.Entities.BusinessUnit, SalesABuId);
                        return user;
                    }

                    return null;
                },
                OnRetrieveMultiple = q => new EntityCollection(),
                OnCreate = entity => entity.LogicalName == CrmSchema.Entities.OpportunitySalesProcess ? CreatedBpfId : Guid.NewGuid()
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

            new QualifyLeadApiPlugin().Execute(new FakeServiceProvider(ctx, factory));
        }

        [Fact]
        public void QualifyLead_Path1_ReuseAccountAndContact_ZeroCustomerWritesAndValidOutputs()
        {
            var lead = BuildReadyLead(RepUserId);
            var activeAccount = new Entity(CrmSchema.Entities.Account, ExistingAccountId);
            activeAccount[CrmSchema.Account.StateCode] = new OptionSetValue(CrmSchema.Account.State.Active);

            var activeContact = new Entity(CrmSchema.Entities.Contact, ExistingContactId);
            activeContact[CrmSchema.Contact.StateCode] = new OptionSetValue(CrmSchema.Contact.State.Active);
            activeContact[CrmSchema.Contact.ParentCustomerId] = new EntityReference(CrmSchema.Entities.Account, ExistingAccountId);

            var caller = BuildCallerService(lead, existingAccount: activeAccount, existingContact: activeContact);
            var writer = BuildWriterService();
            var ctx = BuildApiContext(RepUserId, existingAccountId: ExistingAccountId, existingContactId: ExistingContactId);

            ExecuteApi(ctx, caller, writer);

            // Zero Account/Contact creations or updates
            Assert.Single(caller.CreatedEntities);
            Assert.Equal(CrmSchema.Entities.Opportunity, caller.CreatedEntities[0].LogicalName);
            Assert.Empty(caller.UpdatedEntities);

            // Verify initial Opportunity invariants
            var opp = caller.CreatedEntities[0];
            Assert.Equal(LeadId, opp.GetAttributeValue<EntityReference>(CrmSchema.Opportunity.OriginatingLead).Id);
            Assert.Equal(ExistingAccountId, opp.GetAttributeValue<EntityReference>(CrmSchema.Opportunity.Account).Id);
            Assert.Equal(ExistingContactId, opp.GetAttributeValue<EntityReference>(CrmSchema.Opportunity.PrimaryContact).Id);
            Assert.Equal(CrmSchema.Opportunity.StageValues.Qualify, opp.GetAttributeValue<OptionSetValue>(CrmSchema.Opportunity.Stage).Value);
            Assert.Equal(10, opp.GetAttributeValue<int>(CrmSchema.Opportunity.Probability));
            Assert.Equal(0m, opp.GetAttributeValue<Money>(CrmSchema.Opportunity.Total).Value);
            Assert.Equal(0m, opp.GetAttributeValue<decimal>(CrmSchema.Opportunity.MaxDiscount));
            Assert.Equal(0, opp.GetAttributeValue<int>(CrmSchema.Opportunity.CommandSequence));
            Assert.Null(opp[CrmSchema.Opportunity.CurrentApproval]);

            // Verify conditional Lead closure via UpdateRequest with IfRowVersionMatches
            var leadCloseReq = caller.ExecutedRequests.OfType<UpdateRequest>().Single();
            Assert.Equal(ConcurrencyBehavior.IfRowVersionMatches, leadCloseReq.ConcurrencyBehavior);
            Assert.Equal("1001", leadCloseReq.Target.RowVersion);
            Assert.Equal(CrmSchema.Lead.State.Inactive, leadCloseReq.Target.GetAttributeValue<OptionSetValue>(CrmSchema.Lead.StateCode).Value);
            Assert.Equal(CrmSchema.Lead.Status.Qualified, leadCloseReq.Target.GetAttributeValue<OptionSetValue>(CrmSchema.Lead.StatusCode).Value);

            // Verify single BPF instance created via writerService
            Assert.Single(writer.CreatedEntities);
            Assert.Equal(CrmSchema.Entities.OpportunitySalesProcess, writer.CreatedEntities[0].LogicalName);
            Assert.Equal(CreatedOpportunityId, writer.CreatedEntities[0].GetAttributeValue<EntityReference>(CrmSchema.Bpf.Opportunity).Id);

            // Verify all 5 OutputParameters
            Assert.Equal(ExistingAccountId, (Guid)ctx.OutputParameters[CrmSchema.Apis.Params.AccountId]);
            Assert.Equal(ExistingContactId, (Guid)ctx.OutputParameters[CrmSchema.Apis.Params.ContactId]);
            Assert.Equal(CreatedOpportunityId, (Guid)ctx.OutputParameters[CrmSchema.Apis.Params.OpportunityId]);
            Assert.False((bool)ctx.OutputParameters[CrmSchema.Apis.Params.WasAlreadyQualified]);
            Assert.Equal(CorrelationId.ToString("D"), (string)ctx.OutputParameters[CrmSchema.Apis.Params.CorrelationId]);
        }

        [Fact]
        public void QualifyLead_Path2_ReuseAccountCreateContact_LeavesReusedAccountUntouched()
        {
            var lead = BuildReadyLead(RepUserId);
            var activeAccount = new Entity(CrmSchema.Entities.Account, ExistingAccountId);
            activeAccount[CrmSchema.Account.StateCode] = new OptionSetValue(CrmSchema.Account.State.Active);

            var caller = BuildCallerService(lead, existingAccount: activeAccount);
            var writer = BuildWriterService();
            var ctx = BuildApiContext(RepUserId, existingAccountId: ExistingAccountId, existingContactId: null);

            ExecuteApi(ctx, caller, writer);

            Assert.Equal(2, caller.CreatedEntities.Count);
            Assert.Equal(CrmSchema.Entities.Contact, caller.CreatedEntities[0].LogicalName);
            Assert.Equal(ExistingAccountId, caller.CreatedEntities[0].GetAttributeValue<EntityReference>(CrmSchema.Contact.ParentCustomerId).Id);
            Assert.Equal(CrmSchema.Entities.Opportunity, caller.CreatedEntities[1].LogicalName);

            // Reused Account MUST NOT be modified
            Assert.Empty(caller.UpdatedEntities);

            Assert.Equal(ExistingAccountId, (Guid)ctx.OutputParameters[CrmSchema.Apis.Params.AccountId]);
            Assert.Equal(CreatedContactId, (Guid)ctx.OutputParameters[CrmSchema.Apis.Params.ContactId]);
            Assert.Equal(CreatedOpportunityId, (Guid)ctx.OutputParameters[CrmSchema.Apis.Params.OpportunityId]);
            Assert.False((bool)ctx.OutputParameters[CrmSchema.Apis.Params.WasAlreadyQualified]);
        }

        [Fact]
        public void QualifyLead_Path3_CreateAccountAndContact_LinksPrimaryContactAndManagerTransfersOwnershipToRep()
        {
            // Lead is owned by Rep, qualified by Manager -> creates under caller, links primarycontactid, then transfers ownerid to Rep
            var lead = BuildReadyLead(RepUserId);
            var caller = BuildCallerService(lead);
            var writer = BuildWriterService();
            var ctx = BuildApiContext(ManagerUserId, existingAccountId: null, existingContactId: null);

            ExecuteApi(ctx, caller, writer);

            // Created: Account, Contact, Opportunity — NONE force foreign ownerid on Create
            Assert.Equal(3, caller.CreatedEntities.Count);
            Assert.Equal(CrmSchema.Entities.Account, caller.CreatedEntities[0].LogicalName);
            Assert.False(caller.CreatedEntities[0].Contains(CrmSchema.Common.OwnerId));
            Assert.Equal(CrmSchema.Entities.Contact, caller.CreatedEntities[1].LogicalName);
            Assert.False(caller.CreatedEntities[1].Contains(CrmSchema.Common.OwnerId));
            Assert.Equal(CrmSchema.Entities.Opportunity, caller.CreatedEntities[2].LogicalName);
            Assert.False(caller.CreatedEntities[2].Contains(CrmSchema.Common.OwnerId));

            // Updates: 1) Account.primarycontactid, 2) Contact.ownerid -> Rep, 3) Account.ownerid -> Rep, 4) Opportunity.ownerid -> Rep
            Assert.Equal(4, caller.UpdatedEntities.Count);
            Assert.Equal(CrmSchema.Entities.Account, caller.UpdatedEntities[0].LogicalName);
            Assert.Equal(CreatedContactId, caller.UpdatedEntities[0].GetAttributeValue<EntityReference>(CrmSchema.Account.PrimaryContactId).Id);

            Assert.Equal(CrmSchema.Entities.Contact, caller.UpdatedEntities[1].LogicalName);
            Assert.Equal(RepUserId, caller.UpdatedEntities[1].GetAttributeValue<EntityReference>(CrmSchema.Common.OwnerId).Id);

            Assert.Equal(CrmSchema.Entities.Account, caller.UpdatedEntities[2].LogicalName);
            Assert.Equal(RepUserId, caller.UpdatedEntities[2].GetAttributeValue<EntityReference>(CrmSchema.Common.OwnerId).Id);

            Assert.Equal(CrmSchema.Entities.Opportunity, caller.UpdatedEntities[3].LogicalName);
            Assert.Equal(RepUserId, caller.UpdatedEntities[3].GetAttributeValue<EntityReference>(CrmSchema.Common.OwnerId).Id);

            Assert.Equal(CreatedAccountId, (Guid)ctx.OutputParameters[CrmSchema.Apis.Params.AccountId]);
            Assert.Equal(CreatedContactId, (Guid)ctx.OutputParameters[CrmSchema.Apis.Params.ContactId]);
            Assert.Equal(CreatedOpportunityId, (Guid)ctx.OutputParameters[CrmSchema.Apis.Params.OpportunityId]);
            Assert.False((bool)ctx.OutputParameters[CrmSchema.Apis.Params.WasAlreadyQualified]);
        }

        [Fact]
        public void QualifyLead_AlreadyQualifiedReadback_ReturnsStoredLinksWithZeroWritesEvenWhenRowVersionOmitted()
        {
            var qualifiedLead = BuildReadyLead(RepUserId);
            qualifiedLead[CrmSchema.Lead.StateCode] = new OptionSetValue(CrmSchema.Lead.State.Inactive);
            qualifiedLead[CrmSchema.Lead.StatusCode] = new OptionSetValue(CrmSchema.Lead.Status.Qualified);
            qualifiedLead[CrmSchema.Lead.Account] = new EntityReference(CrmSchema.Entities.Account, ExistingAccountId);
            qualifiedLead[CrmSchema.Lead.Contact] = new EntityReference(CrmSchema.Entities.Contact, ExistingContactId);
            qualifiedLead[CrmSchema.Lead.Opportunity] = new EntityReference(CrmSchema.Entities.Opportunity, CreatedOpportunityId);

            var caller = BuildCallerService(qualifiedLead);
            var writer = BuildWriterService();
            var ctx = BuildApiContext(RepUserId, rowVersion: null); // omitted/stale RowVersion

            ExecuteApi(ctx, caller, writer);

            Assert.Empty(caller.CreatedEntities);
            Assert.Empty(caller.UpdatedEntities);
            Assert.Empty(writer.CreatedEntities);
            Assert.Equal(ExistingAccountId, (Guid)ctx.OutputParameters[CrmSchema.Apis.Params.AccountId]);
            Assert.Equal(ExistingContactId, (Guid)ctx.OutputParameters[CrmSchema.Apis.Params.ContactId]);
            Assert.Equal(CreatedOpportunityId, (Guid)ctx.OutputParameters[CrmSchema.Apis.Params.OpportunityId]);
            Assert.True((bool)ctx.OutputParameters[CrmSchema.Apis.Params.WasAlreadyQualified]);
            Assert.Equal(CorrelationId.ToString("D"), (string)ctx.OutputParameters[CrmSchema.Apis.Params.CorrelationId]);
        }

        [Fact]
        public void QualifyLead_NegativePaths_EnforceExactErrorCodesAndZeroPartialWrites()
        {
            // 1. Disqualified Lead -> CRM005
            var disqLead = BuildReadyLead(RepUserId);
            disqLead[CrmSchema.Lead.StateCode] = new OptionSetValue(CrmSchema.Lead.State.Inactive);
            disqLead[CrmSchema.Lead.StatusCode] = new OptionSetValue(CrmSchema.Lead.Status.Disqualified);
            AssertCrmError(CrmError.CRM005, () => ExecuteApi(BuildApiContext(RepUserId), BuildCallerService(disqLead)));

            // 2. New (uncontacted) Lead -> CRM002
            var newLead = BuildReadyLead(RepUserId);
            newLead[CrmSchema.Lead.StatusCode] = new OptionSetValue(CrmSchema.Lead.Status.New);
            AssertCrmError(CrmError.CRM002, () => ExecuteApi(BuildApiContext(RepUserId), BuildCallerService(newLead)));

            // 3. Contact without Account -> CRM004 (including Dataverse OData default(Guid) injection when ExistingAccountId is omitted)
            var readyLead = BuildReadyLead(RepUserId);
            AssertCrmError(CrmError.CRM004, () => ExecuteApi(
                BuildApiContext(RepUserId, existingAccountId: null, existingContactId: ExistingContactId),
                BuildCallerService(readyLead)));
            AssertCrmError(CrmError.CRM004, () => ExecuteApi(
                BuildApiContext(RepUserId, existingAccountId: Guid.Empty, existingContactId: ExistingContactId),
                BuildCallerService(readyLead)));
            AssertCrmError(CrmError.CRM002, () => ExecuteApi(
                BuildApiContext(RepUserId, expectedClose: DateTime.MinValue),
                BuildCallerService(readyLead)));

            // 4. Mismatched or Inactive Contact -> CRM004
            var activeAccount = new Entity(CrmSchema.Entities.Account, ExistingAccountId);
            activeAccount[CrmSchema.Account.StateCode] = new OptionSetValue(CrmSchema.Account.State.Active);

            var wrongParentContact = new Entity(CrmSchema.Entities.Contact, ExistingContactId);
            wrongParentContact[CrmSchema.Contact.StateCode] = new OptionSetValue(CrmSchema.Contact.State.Active);
            wrongParentContact[CrmSchema.Contact.ParentCustomerId] = new EntityReference(CrmSchema.Entities.Account, OtherAccountId);
            AssertCrmError(CrmError.CRM004, () => ExecuteApi(
                BuildApiContext(RepUserId, existingAccountId: ExistingAccountId, existingContactId: ExistingContactId),
                BuildCallerService(readyLead, existingAccount: activeAccount, existingContact: wrongParentContact)));

            var inactiveContact = new Entity(CrmSchema.Entities.Contact, ExistingContactId);
            inactiveContact[CrmSchema.Contact.StateCode] = new OptionSetValue(CrmSchema.Contact.State.Inactive);
            inactiveContact[CrmSchema.Contact.ParentCustomerId] = new EntityReference(CrmSchema.Entities.Account, ExistingAccountId);
            AssertCrmError(CrmError.CRM004, () => ExecuteApi(
                BuildApiContext(RepUserId, existingAccountId: ExistingAccountId, existingContactId: ExistingContactId),
                BuildCallerService(readyLead, existingAccount: activeAccount, existingContact: inactiveContact)));

            // 5. Inactive or Inaccessible Account -> CRM003
            var inactiveAccount = new Entity(CrmSchema.Entities.Account, ExistingAccountId);
            inactiveAccount[CrmSchema.Account.StateCode] = new OptionSetValue(CrmSchema.Account.State.Inactive);
            AssertCrmError(CrmError.CRM003, () => ExecuteApi(
                BuildApiContext(RepUserId, existingAccountId: ExistingAccountId),
                BuildCallerService(readyLead, existingAccount: inactiveAccount)));

            AssertCrmError(CrmError.CRM003, () => ExecuteApi(
                BuildApiContext(RepUserId, existingAccountId: ExistingAccountId),
                BuildCallerService(readyLead, existingAccount: activeAccount, deniedAccessIds: new HashSet<Guid> { ExistingAccountId })));

            // 6. Stale or Missing ExpectedRowVersion -> CRM021
            AssertCrmError(CrmError.CRM021, () => ExecuteApi(
                BuildApiContext(RepUserId, rowVersion: null),
                BuildCallerService(readyLead)));
            AssertCrmError(CrmError.CRM021, () => ExecuteApi(
                BuildApiContext(RepUserId, rowVersion: "W/\"9999\""),
                BuildCallerService(readyLead)));

            // 7. Duplicate alternate-key faults (-2147088238 and -2147220937) -> CRM021
            foreach (var dupErrorCode in new[] { -2147088238, -2147220937 })
            {
                var dupCaller = BuildCallerService(
                    readyLead,
                    existingAccount: activeAccount,
                    existingContact: new Entity(CrmSchema.Entities.Contact, ExistingContactId)
                    {
                        [CrmSchema.Contact.StateCode] = new OptionSetValue(CrmSchema.Contact.State.Active),
                        [CrmSchema.Contact.ParentCustomerId] = new EntityReference(CrmSchema.Entities.Account, ExistingAccountId)
                    },
                    onCreateOverride: entity =>
                    {
                        if (entity.LogicalName == CrmSchema.Entities.Opportunity)
                        {
                            throw new FaultException<OrganizationServiceFault>(
                                new OrganizationServiceFault { ErrorCode = dupErrorCode, Message = "Duplicate key vn_originatinglead" },
                                new FaultReason("Duplicate key"));
                        }

                        return Guid.NewGuid();
                    });

                AssertCrmError(CrmError.CRM021, () => ExecuteApi(
                    BuildApiContext(RepUserId, existingAccountId: ExistingAccountId, existingContactId: ExistingContactId),
                    dupCaller));
            }

            // 8. Non-transactional execution -> CRM006
            AssertCrmError(CrmError.CRM006, () => ExecuteApi(
                BuildApiContext(RepUserId, isInTransaction: false),
                BuildCallerService(readyLead)));

            // 9. Unconfigured Internal Writer -> CRM013 BEFORE creating any customer or Opportunity
            var unconfiguredCaller = BuildCallerService(readyLead, configureWriter: false);
            AssertCrmError(CrmError.CRM013, () => ExecuteApi(BuildApiContext(RepUserId), unconfiguredCaller));
            Assert.Empty(unconfiguredCaller.CreatedEntities);
            Assert.Empty(unconfiguredCaller.UpdatedEntities);
        }
    }
}
