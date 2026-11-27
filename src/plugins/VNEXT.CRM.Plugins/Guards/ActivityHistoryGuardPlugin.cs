using System;
using System.Collections.Generic;
using System.ServiceModel;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using VNEXT.CRM.Plugins.Core;
using VNEXT.CRM.Plugins.Services;

namespace VNEXT.CRM.Plugins.Guards
{
    public sealed class ActivityHistoryGuardPlugin : IPlugin
    {
        private const string PreImageName = "PreImage";
        private const string PostQualKeyPrefix = "postqual:";

        private static readonly HashSet<string> AllowedRegardingEntities = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            CrmSchema.Entities.Lead,
            CrmSchema.Entities.Opportunity,
            CrmSchema.Entities.Account,
            CrmSchema.Entities.Contact
        };

        private static readonly HashSet<string> AllowedPhoneCallPartyEntities = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            CrmSchema.Entities.SystemUser,
            CrmSchema.Entities.Contact,
            CrmSchema.Entities.Account
        };

        private static readonly string[] TaskColumns =
        {
            CrmSchema.Task.StateCode,
            CrmSchema.Task.StatusCode,
            CrmSchema.Task.Subject,
            CrmSchema.Task.ScheduledStart,
            CrmSchema.Task.ScheduledEnd,
            CrmSchema.Task.ActualEnd,
            CrmSchema.Task.RegardingObjectId,
            CrmSchema.Task.OwnerId,
            CrmSchema.Task.AutomationKey
        };

        private static readonly string[] PhoneCallColumns =
        {
            CrmSchema.PhoneCall.StateCode,
            CrmSchema.PhoneCall.StatusCode,
            CrmSchema.PhoneCall.Subject,
            CrmSchema.PhoneCall.ScheduledStart,
            CrmSchema.PhoneCall.ScheduledEnd,
            CrmSchema.PhoneCall.ActualEnd,
            CrmSchema.PhoneCall.RegardingObjectId,
            CrmSchema.PhoneCall.OwnerId,
            CrmSchema.PhoneCall.DirectionCode
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

            var correlationId = context.CorrelationId;
            var entityName = context.PrimaryEntityName ?? string.Empty;
            var message = context.MessageName ?? string.Empty;

            bool isTask = string.Equals(entityName, CrmSchema.Entities.Task, StringComparison.OrdinalIgnoreCase);
            bool isPhoneCall = string.Equals(entityName, CrmSchema.Entities.PhoneCall, StringComparison.OrdinalIgnoreCase);
            if (!isTask && !isPhoneCall)
            {
                return;
            }

            if (tracing != null)
            {
                tracing.Trace(
                    "PL03 ActivityHistoryGuardPlugin: Message={0}, Entity={1}, Stage={2}, CorrelationId={3:D}",
                    message,
                    entityName,
                    context.Stage,
                    correlationId);
            }

            var initiatingUserId = AuthorizationService.RequireInitiatingUserId(context);
            var callerService = AuthorizationService.CreateCallerService(factory, context);

            if (string.Equals(message, "Create", StringComparison.OrdinalIgnoreCase))
            {
                var target = GetTargetEntity(context);
                if (target != null)
                {
                    ValidateCreate(target, isTask, callerService, initiatingUserId, correlationId);
                }
            }
            else if (string.Equals(message, "CreateMultiple", StringComparison.OrdinalIgnoreCase))
            {
                var targets = GetTargetEntities(context);
                if (targets != null)
                {
                    foreach (var target in targets.Entities)
                    {
                        ValidateCreate(target, isTask, callerService, initiatingUserId, correlationId);
                    }
                }
            }
            else if (string.Equals(message, "Update", StringComparison.OrdinalIgnoreCase))
            {
                var target = GetTargetEntity(context);
                if (target != null)
                {
                    ValidateUpdate(context, target, isTask, allowPreImage: true, callerService, initiatingUserId, correlationId);
                }
            }
            else if (string.Equals(message, "UpdateMultiple", StringComparison.OrdinalIgnoreCase))
            {
                var targets = GetTargetEntities(context);
                if (targets != null)
                {
                    foreach (var target in targets.Entities)
                    {
                        ValidateUpdate(context, target, isTask, allowPreImage: false, callerService, initiatingUserId, correlationId);
                    }
                }
            }
            else if (IsSetStateMessage(message))
            {
                ValidateSetState(context, isTask, callerService, correlationId);
            }
            else if (string.Equals(message, "Assign", StringComparison.OrdinalIgnoreCase))
            {
                ValidateAssign(context, isTask, callerService, correlationId);
            }
            else if (string.Equals(message, "Delete", StringComparison.OrdinalIgnoreCase))
            {
                ValidateDelete(context, isTask, callerService, correlationId);
            }
        }

        private static void ValidateCreate(
            Entity target,
            bool isTask,
            IOrganizationService callerService,
            Guid initiatingUserId,
            Guid correlationId)
        {
            var state = GetOptionSetInt(target, CrmSchema.Common.StateCode);
            if (state.HasValue && state.Value != 0)
            {
                CrmError.Throw(CrmError.CRM015, "Activities must be created in Open state.", correlationId);
            }

            var status = GetOptionSetInt(target, CrmSchema.Common.StatusCode);
            if (status.HasValue && status.Value != -1 && !IsValidStatusForState(isTask, 0, status.Value))
            {
                CrmError.Throw(CrmError.CRM016, "Activity status reason is not valid for an Open activity.", correlationId);
            }

            var subject = target.GetAttributeValue<string>(CrmSchema.Task.Subject);
            if (string.IsNullOrWhiteSpace(subject))
            {
                CrmError.Throw(CrmError.CRM016, "Activity subject is required.", correlationId);
            }

            var scheduledEnd = target.GetAttributeValue<DateTime?>(CrmSchema.Task.ScheduledEnd);
            if (!scheduledEnd.HasValue)
            {
                CrmError.Throw(CrmError.CRM016, "Activity Scheduled End date is required.", correlationId);
            }

            var scheduledStart = target.GetAttributeValue<DateTime?>(CrmSchema.Task.ScheduledStart);
            ValidateScheduleRange(scheduledStart, scheduledEnd, correlationId);

            var regardingRef = target.GetAttributeValue<EntityReference>(CrmSchema.Task.RegardingObjectId);
            ValidateRegarding(regardingRef, checkAccess: true, callerService, initiatingUserId, correlationId);

            if (isTask)
            {
                ValidateTaskAutomationKeyOnCreate(target, callerService, initiatingUserId, correlationId);
            }
            else
            {
                if (!target.Contains(CrmSchema.PhoneCall.DirectionCode) || target[CrmSchema.PhoneCall.DirectionCode] == null)
                {
                    target[CrmSchema.PhoneCall.DirectionCode] = true;
                }

                ValidatePhoneCallPartyList(
                    target.GetAttributeValue<EntityCollection>(CrmSchema.PhoneCall.From),
                    CrmSchema.PhoneCall.From,
                    callerService,
                    initiatingUserId,
                    correlationId);

                ValidatePhoneCallPartyList(
                    target.GetAttributeValue<EntityCollection>(CrmSchema.PhoneCall.To),
                    CrmSchema.PhoneCall.To,
                    callerService,
                    initiatingUserId,
                    correlationId);
            }
        }

        private static void ValidateUpdate(
            IPluginExecutionContext context,
            Entity target,
            bool isTask,
            bool allowPreImage,
            IOrganizationService callerService,
            Guid initiatingUserId,
            Guid correlationId)
        {
            var entityLogicalName = isTask ? CrmSchema.Entities.Task : CrmSchema.Entities.PhoneCall;
            var activityId = target.Id != Guid.Empty ? target.Id : context.PrimaryEntityId;
            var columns = isTask ? TaskColumns : PhoneCallColumns;

            var existing = LoadExistingActivity(context, entityLogicalName, activityId, columns, allowPreImage, callerService, correlationId);
            var existingState = GetOptionSetInt(existing, CrmSchema.Common.StateCode) ?? 0;
            if (existingState != 0)
            {
                CrmError.Throw(
                    CrmError.CRM015,
                    "Completed or canceled activity history is frozen and cannot be modified or reopened.",
                    correlationId);
            }

            if (target.Contains(CrmSchema.Common.OwnerId))
            {
                var existingOwner = existing.GetAttributeValue<EntityReference>(CrmSchema.Common.OwnerId);
                var newOwner = target.GetAttributeValue<EntityReference>(CrmSchema.Common.OwnerId);
                if (newOwner == null || existingOwner == null || newOwner.Id != existingOwner.Id)
                {
                    CrmError.Throw(CrmError.CRM018, "Direct activity ownership transfer is not permitted.", correlationId);
                }
            }

            if (isTask && target.Contains(CrmSchema.Task.AutomationKey))
            {
                var existingKey = existing.GetAttributeValue<string>(CrmSchema.Task.AutomationKey);
                var newKey = target.GetAttributeValue<string>(CrmSchema.Task.AutomationKey);
                if (!string.Equals(existingKey, newKey, StringComparison.Ordinal))
                {
                    CrmError.Throw(CrmError.CRM015, "Task automation key is immutable after creation.", correlationId);
                }
            }

            var existingRegarding = existing.GetAttributeValue<EntityReference>(CrmSchema.Task.RegardingObjectId);
            var targetState = GetOptionSetInt(target, CrmSchema.Common.StateCode);
            if (targetState.HasValue && !IsValidState(targetState.Value))
            {
                CrmError.Throw(CrmError.CRM016, "Unsupported activity state value.", correlationId);
            }

            bool isClosing = targetState.HasValue && (targetState.Value == 1 || targetState.Value == 2);
            if (isClosing)
            {
                if (target.Contains(CrmSchema.Task.RegardingObjectId))
                {
                    var newRegarding = target.GetAttributeValue<EntityReference>(CrmSchema.Task.RegardingObjectId);
                    if (AreEntityReferencesDifferent(existingRegarding, newRegarding))
                    {
                        CrmError.Throw(
                            CrmError.CRM015,
                            "Activity Regarding target cannot be changed while completing or canceling.",
                            correlationId);
                    }
                }

                if (target.Contains(CrmSchema.Common.StatusCode))
                {
                    var targetStatus = GetOptionSetInt(target, CrmSchema.Common.StatusCode);
                    if (targetStatus.HasValue
                        && targetStatus.Value != -1
                        && !IsValidStatusForState(isTask, targetState.Value, targetStatus.Value))
                    {
                        CrmError.Throw(
                            CrmError.CRM016,
                            "Activity status reason does not match the target completed or canceled state.",
                            correlationId);
                    }
                }

                if (!target.Contains(CrmSchema.Task.ActualEnd)
                    && existing.GetAttributeValue<DateTime?>(CrmSchema.Task.ActualEnd) == null)
                {
                    target[CrmSchema.Task.ActualEnd] = UtcClock.EnsureUtc(DateTime.UtcNow);
                }
            }
            else if (target.Contains(CrmSchema.Common.StatusCode))
            {
                var targetStatus = GetOptionSetInt(target, CrmSchema.Common.StatusCode);
                if (targetStatus.HasValue
                    && targetStatus.Value != -1
                    && !IsValidStatusForState(isTask, 0, targetStatus.Value))
                {
                    CrmError.Throw(CrmError.CRM016, "Activity status reason is not valid for an Open activity.", correlationId);
                }
            }

            var merged = MergeOverExisting(entityLogicalName, activityId, existing, target);

            var mergedSubject = merged.GetAttributeValue<string>(CrmSchema.Task.Subject);
            if (string.IsNullOrWhiteSpace(mergedSubject))
            {
                CrmError.Throw(CrmError.CRM016, "Activity subject cannot be cleared.", correlationId);
            }

            var mergedScheduledEnd = merged.GetAttributeValue<DateTime?>(CrmSchema.Task.ScheduledEnd);
            if (!mergedScheduledEnd.HasValue)
            {
                CrmError.Throw(CrmError.CRM016, "Activity Scheduled End date cannot be cleared.", correlationId);
            }

            var mergedScheduledStart = merged.GetAttributeValue<DateTime?>(CrmSchema.Task.ScheduledStart);
            ValidateScheduleRange(mergedScheduledStart, mergedScheduledEnd, correlationId);

            var mergedRegarding = merged.GetAttributeValue<EntityReference>(CrmSchema.Task.RegardingObjectId);
            bool regardingChanged = target.Contains(CrmSchema.Task.RegardingObjectId)
                && AreEntityReferencesDifferent(existingRegarding, mergedRegarding);
            ValidateRegarding(mergedRegarding, checkAccess: regardingChanged, callerService, initiatingUserId, correlationId);

            if (!isTask)
            {
                if (target.Contains(CrmSchema.PhoneCall.DirectionCode) && target[CrmSchema.PhoneCall.DirectionCode] == null)
                {
                    CrmError.Throw(CrmError.CRM016, "Phone Call direction cannot be cleared.", correlationId);
                }

                if (target.Contains(CrmSchema.PhoneCall.From))
                {
                    ValidatePhoneCallPartyList(
                        target.GetAttributeValue<EntityCollection>(CrmSchema.PhoneCall.From),
                        CrmSchema.PhoneCall.From,
                        callerService,
                        initiatingUserId,
                        correlationId);
                }

                if (target.Contains(CrmSchema.PhoneCall.To))
                {
                    ValidatePhoneCallPartyList(
                        target.GetAttributeValue<EntityCollection>(CrmSchema.PhoneCall.To),
                        CrmSchema.PhoneCall.To,
                        callerService,
                        initiatingUserId,
                        correlationId);
                }
            }
        }

        private static void ValidateSetState(
            IPluginExecutionContext context,
            bool isTask,
            IOrganizationService callerService,
            Guid correlationId)
        {
            var entityLogicalName = isTask ? CrmSchema.Entities.Task : CrmSchema.Entities.PhoneCall;
            var moniker = GetEntityMoniker(context);
            var activityId = moniker != null ? moniker.Id : context.PrimaryEntityId;

            var existing = LoadExistingActivity(
                context,
                entityLogicalName,
                activityId,
                new[] { CrmSchema.Common.StateCode },
                allowPreImage: true,
                callerService,
                correlationId);

            var existingState = GetOptionSetInt(existing, CrmSchema.Common.StateCode) ?? 0;
            if (existingState != 0)
            {
                CrmError.Throw(
                    CrmError.CRM015,
                    "Completed or canceled activity history is frozen and cannot be reopened or changed.",
                    correlationId);
            }

            var targetState = GetInputOptionSetInt(context, "State");
            if (!targetState.HasValue || !IsValidState(targetState.Value))
            {
                CrmError.Throw(CrmError.CRM016, "Unsupported target activity state.", correlationId);
            }

            var targetStatus = GetInputOptionSetInt(context, "Status");
            if (targetStatus.HasValue
                && targetStatus.Value != -1
                && !IsValidStatusForState(isTask, targetState.Value, targetStatus.Value))
            {
                CrmError.Throw(CrmError.CRM016, "Activity status reason does not match the requested state.", correlationId);
            }
        }

        private static void ValidateAssign(
            IPluginExecutionContext context,
            bool isTask,
            IOrganizationService callerService,
            Guid correlationId)
        {
            var entityLogicalName = isTask ? CrmSchema.Entities.Task : CrmSchema.Entities.PhoneCall;
            var targetRef = GetTargetReference(context);
            var activityId = targetRef != null ? targetRef.Id : context.PrimaryEntityId;

            var existing = LoadExistingActivity(
                context,
                entityLogicalName,
                activityId,
                new[] { CrmSchema.Common.StateCode },
                allowPreImage: true,
                callerService,
                correlationId);

            var existingState = GetOptionSetInt(existing, CrmSchema.Common.StateCode) ?? 0;
            if (existingState != 0)
            {
                CrmError.Throw(CrmError.CRM015, "Completed or canceled activities cannot be reassigned.", correlationId);
            }

            CrmError.Throw(CrmError.CRM018, "Direct activity reassignment is not permitted.", correlationId);
        }

        private static void ValidateDelete(
            IPluginExecutionContext context,
            bool isTask,
            IOrganizationService callerService,
            Guid correlationId)
        {
            var entityLogicalName = isTask ? CrmSchema.Entities.Task : CrmSchema.Entities.PhoneCall;
            var targetRef = GetTargetReference(context);
            var activityId = targetRef != null ? targetRef.Id : context.PrimaryEntityId;

            var columns = isTask
                ? new[] { CrmSchema.Common.StateCode, CrmSchema.Task.AutomationKey }
                : new[] { CrmSchema.Common.StateCode };

            var existing = LoadExistingActivity(
                context,
                entityLogicalName,
                activityId,
                columns,
                allowPreImage: true,
                callerService,
                correlationId);

            var existingState = GetOptionSetInt(existing, CrmSchema.Common.StateCode) ?? 0;
            if (existingState != 0)
            {
                CrmError.Throw(CrmError.CRM015, "Completed or canceled activity history cannot be deleted.", correlationId);
            }

            if (isTask)
            {
                var automationKey = existing.GetAttributeValue<string>(CrmSchema.Task.AutomationKey);
                if (!string.IsNullOrWhiteSpace(automationKey))
                {
                    CrmError.Throw(CrmError.CRM015, "System-generated Tasks with an automation key cannot be deleted.", correlationId);
                }
            }
        }

        private static void ValidateRegarding(
            EntityReference regardingRef,
            bool checkAccess,
            IOrganizationService callerService,
            Guid initiatingUserId,
            Guid correlationId)
        {
            if (regardingRef == null
                || regardingRef.Id == Guid.Empty
                || string.IsNullOrWhiteSpace(regardingRef.LogicalName)
                || !AllowedRegardingEntities.Contains(regardingRef.LogicalName))
            {
                CrmError.Throw(
                    CrmError.CRM016,
                    "Activity Regarding must reference a supported Lead, Opportunity, Account, or Contact record.",
                    correlationId);
            }

            if (checkAccess)
            {
                AuthorizationService.EnsureAccessRights(
                    callerService,
                    initiatingUserId,
                    regardingRef,
                    AccessRights.ReadAccess | AccessRights.AppendToAccess,
                    CrmError.CRM016,
                    "Caller lacks required access to the Regarding record.",
                    correlationId);
            }
        }

        private static void ValidateScheduleRange(
            DateTime? scheduledStart,
            DateTime? scheduledEnd,
            Guid correlationId)
        {
            if (scheduledStart.HasValue && scheduledEnd.HasValue)
            {
                var startUtc = UtcClock.EnsureUtc(scheduledStart.Value);
                var endUtc = UtcClock.EnsureUtc(scheduledEnd.Value);
                if (startUtc > endUtc)
                {
                    CrmError.Throw(
                        CrmError.CRM016,
                        "Activity Scheduled Start cannot be later than Scheduled End.",
                        correlationId);
                }
            }
        }

        private static void ValidateTaskAutomationKeyOnCreate(
            Entity target,
            IOrganizationService callerService,
            Guid initiatingUserId,
            Guid correlationId)
        {
            if (!target.Contains(CrmSchema.Task.AutomationKey))
            {
                return;
            }

            var rawKey = target.GetAttributeValue<string>(CrmSchema.Task.AutomationKey);
            if (string.IsNullOrWhiteSpace(rawKey))
            {
                return;
            }

            if (!rawKey.StartsWith(PostQualKeyPrefix, StringComparison.Ordinal))
            {
                CrmError.Throw(CrmError.CRM015, "Unrecognized Task automation key format is not permitted.", correlationId);
            }

            var suffix = rawKey.Substring(PostQualKeyPrefix.Length);
            Guid parsedGuid;
            if (string.IsNullOrWhiteSpace(suffix)
                || ContainsWhitespace(suffix)
                || !Guid.TryParse(suffix, out parsedGuid)
                || parsedGuid == Guid.Empty)
            {
                CrmError.Throw(CrmError.CRM015, "Invalid post-qualification Task automation key format.", correlationId);
            }

            bool isSalesUser;
            try
            {
                isSalesUser = AuthorizationService.UserHasRole(callerService, initiatingUserId, CrmSchema.Roles.SalesRepresentative)
                    || AuthorizationService.UserHasRole(callerService, initiatingUserId, CrmSchema.Roles.SalesManager);
            }
            catch (FaultException<OrganizationServiceFault>)
            {
                CrmError.Throw(
                    CrmError.CRM015,
                    "Unable to verify caller role for Task automation key assignment.",
                    correlationId);
                return;
            }

            if (isSalesUser)
            {
                CrmError.Throw(CrmError.CRM015, "Sales users cannot manually assign a post-qualification automation key.", correlationId);
            }
        }

        private static void ValidatePhoneCallPartyList(
            EntityCollection parties,
            string fieldName,
            IOrganizationService callerService,
            Guid initiatingUserId,
            Guid correlationId)
        {
            if (parties == null || parties.Entities == null || parties.Entities.Count == 0)
            {
                CrmError.Throw(
                    CrmError.CRM016,
                    string.Format("Phone Call '{0}' participant list must contain at least one valid party.", fieldName),
                    correlationId);
            }

            foreach (var party in parties.Entities)
            {
                if (party == null)
                {
                    CrmError.Throw(CrmError.CRM016, "Phone Call participant entry cannot be null.", correlationId);
                }

                var partyRef = party.GetAttributeValue<EntityReference>(CrmSchema.ActivityParty.PartyId);
                if (partyRef != null)
                {
                    if (partyRef.Id == Guid.Empty
                        || string.IsNullOrWhiteSpace(partyRef.LogicalName)
                        || !AllowedPhoneCallPartyEntities.Contains(partyRef.LogicalName))
                    {
                        CrmError.Throw(
                            CrmError.CRM016,
                            "Phone Call resolved participants must be System User, Contact, or Account only.",
                            correlationId);
                    }

                    if (string.Equals(partyRef.LogicalName, CrmSchema.Entities.Contact, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(partyRef.LogicalName, CrmSchema.Entities.Account, StringComparison.OrdinalIgnoreCase))
                    {
                        AuthorizationService.EnsureAccessRights(
                            callerService,
                            initiatingUserId,
                            partyRef,
                            AccessRights.ReadAccess,
                            CrmError.CRM016,
                            "Caller lacks Read access to a Phone Call participant.",
                            correlationId);
                    }
                }
                else
                {
                    var addressUsed = party.GetAttributeValue<string>(CrmSchema.ActivityParty.AddressUsed);
                    if (string.IsNullOrWhiteSpace(addressUsed))
                    {
                        CrmError.Throw(
                            CrmError.CRM016,
                            "Unresolved Phone Call participants require a nonblank addressused value.",
                            correlationId);
                    }
                }
            }
        }

        private static bool IsValidState(int state)
        {
            return state == 0 || state == 1 || state == 2;
        }

        private static bool IsValidStatusForState(bool isTask, int state, int status)
        {
            if (isTask)
            {
                switch (state)
                {
                    case CrmSchema.Task.State.Open:
                        return status == CrmSchema.Task.Status.NotStarted
                            || status == CrmSchema.Task.Status.InProgress
                            || status == CrmSchema.Task.Status.WaitingOnSomeoneElse
                            || status == CrmSchema.Task.Status.Deferred;
                    case CrmSchema.Task.State.Completed:
                        return status == CrmSchema.Task.Status.Completed;
                    case CrmSchema.Task.State.Canceled:
                        return status == CrmSchema.Task.Status.Canceled;
                    default:
                        return false;
                }
            }

            switch (state)
            {
                case CrmSchema.PhoneCall.State.Open:
                    return status == CrmSchema.PhoneCall.Status.Open;
                case CrmSchema.PhoneCall.State.Completed:
                    return status == CrmSchema.PhoneCall.Status.Made
                        || status == CrmSchema.PhoneCall.Status.Received;
                case CrmSchema.PhoneCall.State.Canceled:
                    return status == CrmSchema.PhoneCall.Status.Canceled;
                default:
                    return false;
            }
        }

        private static bool ContainsWhitespace(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                if (char.IsWhiteSpace(value[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool AreEntityReferencesDifferent(EntityReference first, EntityReference second)
        {
            if (first == null && second == null)
            {
                return false;
            }

            if (first == null || second == null)
            {
                return true;
            }

            return first.Id != second.Id
                || !string.Equals(first.LogicalName, second.LogicalName, StringComparison.OrdinalIgnoreCase);
        }

        private static Entity LoadExistingActivity(
            IPluginExecutionContext context,
            string entityLogicalName,
            Guid activityId,
            string[] columns,
            bool allowPreImage,
            IOrganizationService callerService,
            Guid correlationId)
        {
            if (allowPreImage
                && context.PreEntityImages != null
                && context.PreEntityImages.Contains(PreImageName)
                && context.PreEntityImages[PreImageName] != null)
            {
                return context.PreEntityImages[PreImageName];
            }

            if (activityId == Guid.Empty)
            {
                CrmError.Throw(CrmError.CRM016, "Activity identifier is missing.", correlationId);
            }

            try
            {
                var entity = callerService.Retrieve(entityLogicalName, activityId, new ColumnSet(columns));
                if (entity == null)
                {
                    CrmError.Throw(CrmError.CRM016, "Existing activity record was not found.", correlationId);
                }

                return entity;
            }
            catch (FaultException<OrganizationServiceFault>)
            {
                CrmError.Throw(CrmError.CRM016, "Existing activity record could not be accessed.", correlationId);
                return null;
            }
        }

        private static Entity MergeOverExisting(string entityName, Guid id, Entity existing, Entity target)
        {
            var merged = new Entity(entityName, id);
            if (existing != null)
            {
                foreach (var kvp in existing.Attributes)
                {
                    merged[kvp.Key] = kvp.Value;
                }
            }

            if (target != null)
            {
                foreach (var kvp in target.Attributes)
                {
                    merged[kvp.Key] = kvp.Value;
                }
            }

            return merged;
        }

        private static Entity GetTargetEntity(IPluginExecutionContext context)
        {
            if (context.InputParameters != null && context.InputParameters.Contains("Target"))
            {
                return context.InputParameters["Target"] as Entity;
            }

            return null;
        }

        private static EntityCollection GetTargetEntities(IPluginExecutionContext context)
        {
            if (context.InputParameters != null && context.InputParameters.Contains("Targets"))
            {
                return context.InputParameters["Targets"] as EntityCollection;
            }

            return null;
        }

        private static EntityReference GetTargetReference(IPluginExecutionContext context)
        {
            if (context.InputParameters != null && context.InputParameters.Contains("Target"))
            {
                var targetRef = context.InputParameters["Target"] as EntityReference;
                if (targetRef != null)
                {
                    return targetRef;
                }
            }

            if (context.PrimaryEntityId != Guid.Empty && !string.IsNullOrWhiteSpace(context.PrimaryEntityName))
            {
                return new EntityReference(context.PrimaryEntityName, context.PrimaryEntityId);
            }

            return null;
        }

        private static EntityReference GetEntityMoniker(IPluginExecutionContext context)
        {
            if (context.InputParameters != null && context.InputParameters.Contains("EntityMoniker"))
            {
                return context.InputParameters["EntityMoniker"] as EntityReference;
            }

            return null;
        }

        private static bool IsSetStateMessage(string message)
        {
            return string.Equals(message, "SetState", StringComparison.OrdinalIgnoreCase)
                || string.Equals(message, "SetStateDynamicEntity", StringComparison.OrdinalIgnoreCase);
        }

        private static int? GetOptionSetInt(Entity entity, string attributeName)
        {
            var opt = entity.GetAttributeValue<OptionSetValue>(attributeName);
            return opt != null ? (int?)opt.Value : null;
        }

        private static int? GetInputOptionSetInt(IPluginExecutionContext context, string paramName)
        {
            if (context.InputParameters != null && context.InputParameters.Contains(paramName))
            {
                var raw = context.InputParameters[paramName];
                var opt = raw as OptionSetValue;
                if (opt != null)
                {
                    return opt.Value;
                }

                if (raw is int)
                {
                    return (int)raw;
                }

                var str = raw as string;
                if (!string.IsNullOrWhiteSpace(str))
                {
                    var trimmed = str.Trim();
                    int parsed;
                    if (int.TryParse(trimmed, out parsed))
                    {
                        return parsed;
                    }

                    if (string.Equals(paramName, "State", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.Equals(trimmed, "Open", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(trimmed, "Active", StringComparison.OrdinalIgnoreCase))
                        {
                            return 0;
                        }

                        if (string.Equals(trimmed, "Completed", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(trimmed, "Inactive", StringComparison.OrdinalIgnoreCase))
                        {
                            return 1;
                        }

                        if (string.Equals(trimmed, "Canceled", StringComparison.OrdinalIgnoreCase))
                        {
                            return 2;
                        }
                    }
                }
            }

            return null;
        }
    }
}
