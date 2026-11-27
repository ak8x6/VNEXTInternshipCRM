using System;
using System.ServiceModel;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using VNEXT.CRM.Plugins.Core;

namespace VNEXT.CRM.Plugins.Services
{
    public static class AuthorizationService
    {
        private const string SystemUserFullNameAttribute = "fullname";

        public static Guid RequireInitiatingUserId(IPluginExecutionContext context)
        {
            if (context == null || context.InitiatingUserId == Guid.Empty)
            {
                CrmError.Throw(
                    CrmError.CRM013,
                    "Initiating user identity could not be verified.",
                    context != null ? context.CorrelationId : Guid.Empty);
            }

            return context.InitiatingUserId;
        }

        public static IOrganizationService CreateCallerService(
            IOrganizationServiceFactory factory,
            IPluginExecutionContext context)
        {
            var initiatingUserId = RequireInitiatingUserId(context);

            if (factory == null)
            {
                CrmError.Throw(
                    CrmError.CRM013,
                    "Organization service factory is unavailable.",
                    context.CorrelationId);
            }

            var callerService = factory.CreateOrganizationService(initiatingUserId);
            if (callerService == null)
            {
                CrmError.Throw(
                    CrmError.CRM013,
                    "Caller organization service could not be created.",
                    context.CorrelationId);
            }

            return callerService;
        }

        public static IOrganizationService CreateInternalWriterService(
            IOrganizationServiceFactory factory,
            CrmPolicyConfig config,
            Guid correlationId)
        {
            if (factory == null
                || config == null
                || !config.InternalWriterUserId.HasValue
                || config.InternalWriterUserId.Value == Guid.Empty)
            {
                CrmError.Throw(
                    CrmError.CRM013,
                    "Scoped internal writer is not configured.",
                    correlationId);
            }

            var writerUserId = config.InternalWriterUserId.Value;
            var writerService = factory.CreateOrganizationService(writerUserId);
            if (writerService == null)
            {
                CrmError.Throw(
                    CrmError.CRM013,
                    "Scoped internal writer is disabled or unavailable.",
                    correlationId);
            }

            try
            {
                var writerUser = writerService.Retrieve(
                    CrmSchema.Entities.SystemUser,
                    writerUserId,
                    new ColumnSet(CrmSchema.Platform.IsDisabled, CrmSchema.Platform.BusinessUnitId));

                if (writerUser == null || writerUser.GetAttributeValue<bool?>(CrmSchema.Platform.IsDisabled) == true)
                {
                    CrmError.Throw(
                        CrmError.CRM013,
                        "Scoped internal writer is disabled or unavailable.",
                        correlationId);
                }
            }
            catch (FaultException<OrganizationServiceFault>)
            {
                CrmError.Throw(
                    CrmError.CRM013,
                    "Scoped internal writer is disabled or unavailable.",
                    correlationId);
            }

            return writerService;
        }

        public static AccessRights GetPrincipalAccessRights(
            IOrganizationService service,
            Guid userId,
            EntityReference target)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            var request = new RetrievePrincipalAccessRequest
            {
                Principal = new EntityReference(CrmSchema.Entities.SystemUser, userId),
                Target = target
            };

            var response = (RetrievePrincipalAccessResponse)service.Execute(request);
            return response.AccessRights;
        }

        public static void EnsureAccessRights(
            IOrganizationService service,
            Guid userId,
            EntityReference target,
            AccessRights requiredRights,
            string errorCode,
            string denialMessage,
            Guid correlationId)
        {
            if (service == null || userId == Guid.Empty || target == null || target.Id == Guid.Empty)
            {
                CrmError.Throw(errorCode, denialMessage, correlationId);
            }

            try
            {
                var rights = GetPrincipalAccessRights(service, userId, target);
                if ((rights & requiredRights) != requiredRights)
                {
                    CrmError.Throw(errorCode, denialMessage, correlationId);
                }
            }
            catch (FaultException<OrganizationServiceFault>)
            {
                CrmError.Throw(errorCode, denialMessage, correlationId);
            }
        }

        public static Entity RetrieveEnabledUser(
            IOrganizationService service,
            Guid userId,
            Guid correlationId)
        {
            if (service == null || userId == Guid.Empty)
            {
                CrmError.Throw(CrmError.CRM013, "User account is disabled or unavailable.", correlationId);
            }

            try
            {
                var user = service.Retrieve(
                    CrmSchema.Entities.SystemUser,
                    userId,
                    new ColumnSet(
                        CrmSchema.Platform.SystemUserId,
                        CrmSchema.Platform.IsDisabled,
                        CrmSchema.Platform.BusinessUnitId,
                        CrmSchema.Platform.ParentSystemUserId,
                        SystemUserFullNameAttribute));

                if (user == null || user.GetAttributeValue<bool?>(CrmSchema.Platform.IsDisabled) == true)
                {
                    CrmError.Throw(CrmError.CRM013, "User account is disabled or unavailable.", correlationId);
                }

                return user;
            }
            catch (FaultException<OrganizationServiceFault>)
            {
                CrmError.Throw(CrmError.CRM013, "User account is disabled or unavailable.", correlationId);
                return null;
            }
        }

        public static bool UserHasRole(IOrganizationService service, Guid userId, string roleName)
        {
            if (service == null || userId == Guid.Empty || string.IsNullOrWhiteSpace(roleName))
            {
                return false;
            }

            var query = new QueryExpression(CrmSchema.Entities.Role)
            {
                ColumnSet = new ColumnSet(CrmSchema.Platform.RoleName),
                TopCount = 1,
                NoLock = true
            };

            query.Criteria.AddCondition(
                CrmSchema.Platform.RoleName,
                ConditionOperator.Equal,
                roleName.Trim());

            var userRolesLink = query.AddLink(
                CrmSchema.Entities.SystemUserRoles,
                CrmSchema.Platform.RoleId,
                CrmSchema.Platform.RoleId,
                JoinOperator.Inner);

            userRolesLink.LinkCriteria.AddCondition(
                CrmSchema.Platform.SystemUserId,
                ConditionOperator.Equal,
                userId);

            var results = service.RetrieveMultiple(query);
            return results != null && results.Entities != null && results.Entities.Count > 0;
        }
    }
}
