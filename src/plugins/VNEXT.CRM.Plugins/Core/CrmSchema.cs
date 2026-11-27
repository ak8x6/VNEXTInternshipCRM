using System;

namespace VNEXT.CRM.Plugins.Core
{
    public static class CrmSchema
    {
        public static class Entities
        {
            public const string Lead = "vn_lead";
            public const string Opportunity = "vn_opportunity";
            public const string Product = "vn_product";
            public const string OpportunityLine = "vn_opportunityline";
            public const string ApprovalRequest = "vn_approvalrequest";
            public const string OpportunitySalesProcess = "vn_opportunitysalesprocess";
            public const string Account = "account";
            public const string Contact = "contact";
            public const string Task = "task";
            public const string PhoneCall = "phonecall";
            public const string ActivityParty = "activityparty";
            public const string SystemUser = "systemuser";
            public const string BusinessUnit = "businessunit";
            public const string TransactionCurrency = "transactioncurrency";
            public const string Organization = "organization";
            public const string EnvironmentVariableDefinition = "environmentvariabledefinition";
            public const string EnvironmentVariableValue = "environmentvariablevalue";
            public const string ProcessStage = "processstage";
            public const string Workflow = "workflow";
            public const string Role = "role";
            public const string SystemUserRoles = "systemuserroles";
        }

        public static class Roles
        {
            public const string SalesRepresentative = "Sales Representative";
            public const string SalesManager = "Sales Manager";
            public const string SalesAdministrator = "Sales Administrator";
            public const string InternalWriter = "VNEXT Internal CRM Writer";
        }

        public static class EnvVars
        {
            public const string ApprovalAmount = "vn_ApprovalAmount";
            public const string ApprovalDiscount = "vn_ApprovalDiscount";
            public const string MaxDiscount = "vn_MaxDiscount";
            public const string StaleDays = "vn_StaleDays";
            public const string EscalationDays = "vn_EscalationDays";
            public const string InternalWriterUserId = "vn_InternalWriterUserId";
        }

        public static class Apis
        {
            public const string QualifyLead = "vn_QualifyLead";
            public const string TransitionOpportunity = "vn_TransitionOpportunity";
            public const string EditOpportunityLine = "vn_EditOpportunityLine";
            public const string ManageDealApproval = "vn_ManageDealApproval";

            public static class Params
            {
                public const string Target = "Target";
                public const string ExpectedRowVersion = "ExpectedRowVersion";
                public const string OpportunityName = "OpportunityName";
                public const string ExpectedCloseDate = "ExpectedCloseDate";
                public const string ExistingAccountId = "ExistingAccountId";
                public const string ExistingContactId = "ExistingContactId";
                public const string AccountId = "AccountId";
                public const string ContactId = "ContactId";
                public const string OpportunityId = "OpportunityId";
                public const string WasAlreadyQualified = "WasAlreadyQualified";
                public const string CorrelationId = "CorrelationId";
                public const string Command = "Command";
                public const string TargetStage = "TargetStage";
                public const string Comments = "Comments";
                public const string ActualCloseDate = "ActualCloseDate";
                public const string LossReason = "LossReason";
                public const string Stage = "Stage";
                public const string StatusReason = "StatusReason";
                public const string CommandSequence = "CommandSequence";
                public const string Operation = "Operation";
                public const string LineId = "LineId";
                public const string ProductId = "ProductId";
                public const string Quantity = "Quantity";
                public const string DiscountPercent = "DiscountPercent";
                public const string Total = "Total";
                public const string WasAlreadyApplied = "WasAlreadyApplied";
                public const string RequestId = "RequestId";
                public const string ResponderEntraId = "ResponderEntraId";
                public const string Outcome = "Outcome";
                public const string Result = "Result";
                public const string Accepted = "Accepted";
                public const string Reason = "Reason";
            }
        }

        public static class Common
        {
            public const string StateCode = "statecode";
            public const string StatusCode = "statuscode";
            public const string OwnerId = "ownerid";
            public const string OwningBusinessUnit = "owningbusinessunit";
            public const string TransactionCurrencyId = "transactioncurrencyid";
            public const string CreatedOn = "createdon";
            public const string ModifiedOn = "modifiedon";
            public const string VersionNumber = "versionnumber";
        }

        public static class Bpf
        {
            public const string UniqueName = "vn_opportunitysalesprocess";
            public static readonly Guid WorkflowId = new Guid("b8840bdc-dd2f-49ce-80e4-03f3c8b863c5");
            public static readonly Guid StageQualifyId = new Guid("0a384b78-1d2f-493b-9b3d-b7dd0cef7369");
            public static readonly Guid StageDevelopId = new Guid("517c39ca-5d1b-42e5-ba32-4990b4358bf9");
            public static readonly Guid StageProposeId = new Guid("8cc3a2e7-70c5-4c8a-bc7a-90efa996e46c");
            public static readonly Guid StageCloseId = new Guid("2d71ef5f-16b5-4f7c-a6c1-3fd7bd97f81c");

            public const string Id = "businessprocessflowinstanceid";
            public const string Name = "bpf_name";
            public const string Opportunity = "bpf_vn_opportunityid";
            public const string ActiveStageId = "activestageid";
            public const string ProcessId = "processid";
            public const string TraversedPath = "traversedpath";
            public const string ActiveStageStartedOn = "activestagestartedon";
            public const string CompletedOn = "completedon";
            public const string StateCode = "statecode";
            public const string StatusCode = "statuscode";

            public const int StateActive = 0;
            public const int StateInactive = 1;
            public const int StatusActive = 1;
            public const int StatusFinished = 2;
            public const int StatusAborted = 3;
        }

        public static class Lead
        {
            public const string Id = "vn_leadid";
            public const string Name = "vn_name";
            public const string StateCode = "statecode";
            public const string StatusCode = "statuscode";
            public const string FirstName = "vn_firstname";
            public const string LastName = "vn_lastname";
            public const string CompanyName = "vn_companyname";
            public const string Email = "vn_email";
            public const string Phone = "vn_phone";
            public const string Source = "vn_source";
            public const string Rating = "vn_rating";
            public const string Need = "vn_need";
            public const string NextStep = "vn_nextstep";
            public const string Budget = "vn_budget";
            public const string BuyingTargetDate = "vn_buyingtargetdate";
            public const string NextFollowUpDate = "vn_nextfollowupdate";
            public const string DisqualificationReason = "vn_disqualificationreason";
            public const string DisqualificationComments = "vn_disqualificationcomments";
            public const string DuplicateOf = "vn_duplicateof";
            public const string Account = "vn_account";
            public const string Contact = "vn_contact";
            public const string Opportunity = "vn_opportunity";
            public const string QualifiedOn = "vn_qualifiedon";
            public const string DisqualifiedOn = "vn_disqualifiedon";
            public const string OwnerId = "ownerid";
            public const string OwningBusinessUnit = "owningbusinessunit";
            public const string TransactionCurrencyId = "transactioncurrencyid";

            public static class State
            {
                public const int Active = 0;
                public const int Inactive = 1;
            }

            public static class Status
            {
                public const int New = 100000000;
                public const int Contacted = 100000001;
                public const int Qualified = 100000002;
                public const int Disqualified = 100000003;
            }

            public static class SourceValues
            {
                public const int Website = 100000000;
                public const int Referral = 100000001;
                public const int ColdCall = 100000002;
                public const int Event = 100000003;
            }

            public static class RatingValues
            {
                public const int Hot = 100000000;
                public const int Warm = 100000001;
                public const int Cold = 100000002;
            }

            public static class DisqualificationReasonValues
            {
                public const int NoFit = 100000000;
                public const int NoBudget = 100000001;
                public const int NoResponse = 100000002;
                public const int Duplicate = 100000003;
            }
        }

        public static class Opportunity
        {
            public const string Id = "vn_opportunityid";
            public const string Name = "vn_name";
            public const string StateCode = "statecode";
            public const string StatusCode = "statuscode";
            public const string Account = "vn_account";
            public const string PrimaryContact = "vn_primarycontact";
            public const string OriginatingLead = "vn_originatinglead";
            public const string Stage = "vn_stage";
            public const string Probability = "vn_probability";
            public const string ExpectedCloseDate = "vn_expectedclosedate";
            public const string Need = "vn_need";
            public const string ProposalSummary = "vn_proposalsummary";
            public const string DecisionMaker = "vn_decisionmaker";
            public const string Budget = "vn_budget";
            public const string BuyingTargetDate = "vn_buyingtargetdate";
            public const string NextStep = "vn_nextstep";
            public const string NextFollowUpDate = "vn_nextfollowupdate";
            public const string Total = "vn_total";
            public const string MaxDiscount = "vn_maxdiscount";
            public const string CurrentApproval = "vn_currentapproval";
            public const string LastProgressOn = "vn_lastprogresson";
            public const string StageEnteredOn = "vn_stageenteredon";
            public const string ActualCloseDate = "vn_actualclosedate";
            public const string ClosedAmount = "vn_closedamount";
            public const string CloseComments = "vn_closecomments";
            public const string LossReason = "vn_lossreason";
            public const string CommandSequence = "vn_commandsequence";
            public const string OwnerId = "ownerid";
            public const string OwningBusinessUnit = "owningbusinessunit";
            public const string TransactionCurrencyId = "transactioncurrencyid";

            public static class State
            {
                public const int Active = 0;
                public const int Inactive = 1;
            }

            public static class Status
            {
                public const int Open = 100000000;
                public const int Won = 100000001;
                public const int Lost = 100000002;
            }

            public static class StageValues
            {
                public const int Qualify = 100000000;
                public const int Develop = 100000001;
                public const int Propose = 100000002;
                public const int Close = 100000003;
            }

            public static class Probabilities
            {
                public const int Qualify = 10;
                public const int Develop = 30;
                public const int Propose = 60;
                public const int Close = 90;
                public const int Won = 100;
                public const int Lost = 0;
            }

            public static class LossReasonValues
            {
                public const int Price = 100000000;
                public const int Timing = 100000001;
                public const int Competitor = 100000002;
                public const int NoBudget = 100000003;
                public const int NoDecision = 100000004;
                public const int Other = 100000005;
            }
        }

        public static class Product
        {
            public const string Id = "vn_productid";
            public const string Name = "vn_name";
            public const string StateCode = "statecode";
            public const string StatusCode = "statuscode";
            public const string Sku = "vn_sku";
            public const string Description = "vn_description";
            public const string UnitPrice = "vn_unitprice";
            public const string TransactionCurrencyId = "transactioncurrencyid";

            public static class State
            {
                public const int Active = 0;
                public const int Inactive = 1;
            }

            public static class Status
            {
                public const int Active = 1;
                public const int Inactive = 2;
            }
        }

        public static class OpportunityLine
        {
            public const string Id = "vn_opportunitylineid";
            public const string Name = "vn_name";
            public const string StateCode = "statecode";
            public const string StatusCode = "statuscode";
            public const string Opportunity = "vn_opportunity";
            public const string Product = "vn_product";
            public const string SnapshotSku = "vn_snapshotsku";
            public const string SnapshotName = "vn_snapshotname";
            public const string UnitPrice = "vn_unitprice";
            public const string Quantity = "vn_quantity";
            public const string DiscountPercent = "vn_discountpercent";
            public const string GrossAmount = "vn_grossamount";
            public const string DiscountAmount = "vn_discountamount";
            public const string NetAmount = "vn_netamount";
            public const string OwnerId = "ownerid";
            public const string TransactionCurrencyId = "transactioncurrencyid";

            public static class State
            {
                public const int Active = 0;
                public const int Inactive = 1;
            }

            public static class Status
            {
                public const int Active = 1;
                public const int Inactive = 2;
            }
        }

        public static class ApprovalRequest
        {
            public const string Id = "vn_approvalrequestid";
            public const string Name = "vn_name";
            public const string StateCode = "statecode";
            public const string StatusCode = "statuscode";
            public const string Result = "vn_result";
            public const string Opportunity = "vn_opportunity";
            public const string DealOwner = "vn_dealowner";
            public const string Approver = "vn_approver";
            public const string Total = "vn_total";
            public const string MaxDiscount = "vn_maxdiscount";
            public const string SubmittedOn = "vn_submittedon";
            public const string Responder = "vn_responder";
            public const string RespondedOn = "vn_respondedon";
            public const string ResponseComments = "vn_responsecomments";
            public const string OwnerId = "ownerid";
            public const string TransactionCurrencyId = "transactioncurrencyid";

            public static class State
            {
                public const int Active = 0;
                public const int Inactive = 1;
            }

            public static class Status
            {
                public const int Active = 1;
                public const int Inactive = 2;
            }

            public static class ResultValues
            {
                public const int Pending = 100000000;
                public const int Approved = 100000001;
                public const int Rejected = 100000002;
                public const int Superseded = 100000003;
                // 100000004 is intentionally unused per DM09
                public const int Cancelled = 100000005;
            }
        }

        public static class Account
        {
            public const string Id = "accountid";
            public const string Name = "name";
            public const string StateCode = "statecode";
            public const string StatusCode = "statuscode";
            public const string PrimaryContactId = "primarycontactid";
            public const string AccountType = "vn_accounttype";
            public const string Industry = "vn_industry";
            public const string OwnerId = "ownerid";
            public const string OwningBusinessUnit = "owningbusinessunit";
            public const string TransactionCurrencyId = "transactioncurrencyid";

            public static class State
            {
                public const int Active = 0;
                public const int Inactive = 1;
            }

            public static class AccountTypeValues
            {
                public const int Prospect = 100000000;
                public const int Customer = 100000001;
                public const int Partner = 100000002;
            }

            public static class IndustryValues
            {
                public const int Manufacturing = 100000000;
                public const int Services = 100000001;
                public const int Retail = 100000002;
                public const int FinancialServices = 100000003;
                public const int Other = 100000004;
            }
        }

        public static class Contact
        {
            public const string Id = "contactid";
            public const string FirstName = "firstname";
            public const string LastName = "lastname";
            public const string FullName = "fullname";
            public const string StateCode = "statecode";
            public const string StatusCode = "statuscode";
            public const string ParentCustomerId = "parentcustomerid";
            public const string EmailAddress1 = "emailaddress1";
            public const string MobilePhone = "mobilephone";
            public const string JobTitle = "jobtitle";
            public const string PreferredMethod = "vn_preferredmethod";
            public const string DoNotContact = "vn_donotcontact";
            public const string OwnerId = "ownerid";
            public const string OwningBusinessUnit = "owningbusinessunit";
            public const string TransactionCurrencyId = "transactioncurrencyid";

            public static class State
            {
                public const int Active = 0;
                public const int Inactive = 1;
            }

            public static class PreferredMethodValues
            {
                public const int Email = 100000000;
                public const int Phone = 100000001;
            }
        }

        public static class Task
        {
            public const string Id = "activityid";
            public const string Subject = "subject";
            public const string Description = "description";
            public const string PriorityCode = "prioritycode";
            public const string ScheduledStart = "scheduledstart";
            public const string ScheduledEnd = "scheduledend";
            public const string ActualEnd = "actualend";
            public const string RegardingObjectId = "regardingobjectid";
            public const string OwnerId = "ownerid";
            public const string StateCode = "statecode";
            public const string StatusCode = "statuscode";
            public const string AutomationKey = "vn_automationkey";

            public static class State
            {
                public const int Open = 0;
                public const int Completed = 1;
                public const int Canceled = 2;
            }

            public static class Status
            {
                public const int NotStarted = 2;
                public const int InProgress = 3;
                public const int WaitingOnSomeoneElse = 4;
                public const int Completed = 5;
                public const int Canceled = 6;
                public const int Deferred = 7;
            }
        }

        public static class PhoneCall
        {
            public const string Id = "activityid";
            public const string Subject = "subject";
            public const string Description = "description";
            public const string PriorityCode = "prioritycode";
            public const string ScheduledStart = "scheduledstart";
            public const string ScheduledEnd = "scheduledend";
            public const string ActualEnd = "actualend";
            public const string RegardingObjectId = "regardingobjectid";
            public const string OwnerId = "ownerid";
            public const string StateCode = "statecode";
            public const string StatusCode = "statuscode";
            public const string DirectionCode = "directioncode";
            public const string From = "from";
            public const string To = "to";
            public const string PhoneNumber = "phonenumber";

            public static class State
            {
                public const int Open = 0;
                public const int Completed = 1;
                public const int Canceled = 2;
            }

            public static class Status
            {
                public const int Open = 1;
                public const int Made = 2;
                public const int Canceled = 3;
                public const int Received = 4;
            }
        }

        public static class ActivityParty
        {
            public const string PartyId = "partyid";
            public const string AddressUsed = "addressused";
            public const string ParticipationTypeMask = "participationtypemask";

            public static class ParticipationType
            {
                public const int Sender = 1;
                public const int ToRecipient = 2;
            }
        }

        public static class Platform
        {
            public const string OrganizationBaseCurrencyId = "basecurrencyid";
            public const string TransactionCurrencyId = "transactioncurrencyid";
            public const string ExchangeRate = "exchangerate";
            public const string EnvVarDefinitionId = "environmentvariabledefinitionid";
            public const string EnvVarSchemaName = "schemaname";
            public const string EnvVarDefaultValue = "defaultvalue";
            public const string EnvVarValue = "value";
            public const string SystemUserId = "systemuserid";
            public const string BusinessUnitId = "businessunitid";
            public const string ParentSystemUserId = "parentsystemuserid";
            public const string IsDisabled = "isdisabled";
            public const string RoleId = "roleid";
            public const string RoleName = "name";
            public const string ParentRootRoleId = "parentrootroleid";
        }
    }
}
