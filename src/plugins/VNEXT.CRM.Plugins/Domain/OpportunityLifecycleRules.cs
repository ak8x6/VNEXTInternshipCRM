using System;
using VNEXT.CRM.Plugins.Core;

namespace VNEXT.CRM.Plugins.Domain
{
    public enum DealHealthStatus
    {
        Healthy,
        AtRisk,
        Closed,
        DataUnavailable
    }

    public sealed class DealRiskAssessment
    {
        public DealHealthStatus Status { get; }
        public bool IsExpectedCloseOverdue { get; }
        public bool IsStaleProgress { get; }
        public bool IsFollowUpOverdue { get; }

        public DealRiskAssessment(
            DealHealthStatus status,
            bool isExpectedCloseOverdue,
            bool isStaleProgress,
            bool isFollowUpOverdue)
        {
            Status = status;
            IsExpectedCloseOverdue = isExpectedCloseOverdue;
            IsStaleProgress = isStaleProgress;
            IsFollowUpOverdue = isFollowUpOverdue;
        }
    }

    public static class OpportunityLifecycleRules
    {
        public static int GetProbabilityForOpenStage(int stage, Guid correlationId)
        {
            switch (stage)
            {
                case CrmSchema.Opportunity.StageValues.Qualify:
                    return CrmSchema.Opportunity.Probabilities.Qualify;
                case CrmSchema.Opportunity.StageValues.Develop:
                    return CrmSchema.Opportunity.Probabilities.Develop;
                case CrmSchema.Opportunity.StageValues.Propose:
                    return CrmSchema.Opportunity.Probabilities.Propose;
                case CrmSchema.Opportunity.StageValues.Close:
                    return CrmSchema.Opportunity.Probabilities.Close;
                default:
                    CrmError.Throw(CrmError.CRM006, "Unknown Opportunity stage value.", correlationId);
                    return 0;
            }
        }

        public static int GetNextAdjacentStage(int currentStage, int? requestedTargetStage, Guid correlationId)
        {
            int nextStage;
            switch (currentStage)
            {
                case CrmSchema.Opportunity.StageValues.Qualify:
                    nextStage = CrmSchema.Opportunity.StageValues.Develop;
                    break;
                case CrmSchema.Opportunity.StageValues.Develop:
                    nextStage = CrmSchema.Opportunity.StageValues.Propose;
                    break;
                case CrmSchema.Opportunity.StageValues.Propose:
                    nextStage = CrmSchema.Opportunity.StageValues.Close;
                    break;
                case CrmSchema.Opportunity.StageValues.Close:
                    CrmError.Throw(CrmError.CRM006, "Opportunity is already in the Close stage and cannot advance further.", correlationId);
                    return 0;
                default:
                    CrmError.Throw(CrmError.CRM006, "Unknown current Opportunity stage value.", correlationId);
                    return 0;
            }

            if (requestedTargetStage.HasValue && requestedTargetStage.Value != nextStage)
            {
                CrmError.Throw(
                    CrmError.CRM006,
                    "Opportunity stage transitions must move forward to the immediate next stage only.",
                    correlationId);
            }

            return nextStage;
        }

        public static Guid GetBpfStageIdForOpportunityStage(int stage, Guid correlationId)
        {
            switch (stage)
            {
                case CrmSchema.Opportunity.StageValues.Qualify:
                    return CrmSchema.Bpf.StageQualifyId;
                case CrmSchema.Opportunity.StageValues.Develop:
                    return CrmSchema.Bpf.StageDevelopId;
                case CrmSchema.Opportunity.StageValues.Propose:
                    return CrmSchema.Bpf.StageProposeId;
                case CrmSchema.Opportunity.StageValues.Close:
                    return CrmSchema.Bpf.StageCloseId;
                default:
                    CrmError.Throw(CrmError.CRM006, "Unknown Opportunity stage value for BPF mapping.", correlationId);
                    return Guid.Empty;
            }
        }

        public static string BuildTraversedPathForStage(int stage, Guid correlationId)
        {
            var qualify = CrmSchema.Bpf.StageQualifyId.ToString("D").ToLowerInvariant();
            var develop = CrmSchema.Bpf.StageDevelopId.ToString("D").ToLowerInvariant();
            var propose = CrmSchema.Bpf.StageProposeId.ToString("D").ToLowerInvariant();
            var close = CrmSchema.Bpf.StageCloseId.ToString("D").ToLowerInvariant();

            switch (stage)
            {
                case CrmSchema.Opportunity.StageValues.Qualify:
                    return qualify;
                case CrmSchema.Opportunity.StageValues.Develop:
                    return string.Join(",", qualify, develop);
                case CrmSchema.Opportunity.StageValues.Propose:
                    return string.Join(",", qualify, develop, propose);
                case CrmSchema.Opportunity.StageValues.Close:
                    return string.Join(",", qualify, develop, propose, close);
                default:
                    CrmError.Throw(CrmError.CRM006, "Unknown Opportunity stage value for BPF traversed path.", correlationId);
                    return string.Empty;
            }
        }

        public static void ValidateStageAdvanceReadiness(
            int targetStage,
            string need,
            string decisionMaker,
            bool hasActiveMatchingContact,
            string nextStep,
            int lineCount,
            decimal total,
            string proposalSummary,
            DateTime? expectedCloseDate,
            Guid correlationId)
        {
            if (targetStage != CrmSchema.Opportunity.StageValues.Develop
                && targetStage != CrmSchema.Opportunity.StageValues.Propose
                && targetStage != CrmSchema.Opportunity.StageValues.Close)
            {
                CrmError.Throw(CrmError.CRM006, "Invalid target stage for Opportunity stage advance.", correlationId);
            }

            if (string.IsNullOrWhiteSpace(need))
            {
                CrmError.Throw(CrmError.CRM007, "Persisted business need is required before advancing stage.", correlationId);
            }

            if (string.IsNullOrWhiteSpace(decisionMaker))
            {
                CrmError.Throw(CrmError.CRM007, "Persisted decision maker is required before advancing stage.", correlationId);
            }

            if (!hasActiveMatchingContact)
            {
                CrmError.Throw(CrmError.CRM007, "An active primary Contact belonging to the Opportunity Account is required.", correlationId);
            }

            if (string.IsNullOrWhiteSpace(nextStep))
            {
                CrmError.Throw(CrmError.CRM007, "Next step is required before advancing stage.", correlationId);
            }

            if (targetStage >= CrmSchema.Opportunity.StageValues.Propose)
            {
                if (lineCount < 1)
                {
                    CrmError.Throw(CrmError.CRM007, "At least one Opportunity Line is required to enter Propose or Close.", correlationId);
                }

                if (total <= 0m)
                {
                    CrmError.Throw(CrmError.CRM007, "A positive Opportunity Total is required to enter Propose or Close.", correlationId);
                }

                if (string.IsNullOrWhiteSpace(proposalSummary))
                {
                    CrmError.Throw(CrmError.CRM007, "Proposal summary is required to enter Propose or Close.", correlationId);
                }
            }

            if (targetStage == CrmSchema.Opportunity.StageValues.Close)
            {
                if (!expectedCloseDate.HasValue)
                {
                    CrmError.Throw(CrmError.CRM007, "Expected close date is required to enter Close.", correlationId);
                }
            }
        }

        public static void ValidatePersistedOpenStageFields(
            int currentStage,
            string name,
            string need,
            string decisionMaker,
            string proposalSummary,
            string nextStep,
            DateTime? expectedCloseDate,
            DateTime? nextFollowUpDate,
            Guid correlationId)
        {
            if (currentStage != CrmSchema.Opportunity.StageValues.Qualify
                && currentStage != CrmSchema.Opportunity.StageValues.Develop
                && currentStage != CrmSchema.Opportunity.StageValues.Propose
                && currentStage != CrmSchema.Opportunity.StageValues.Close)
            {
                CrmError.Throw(CrmError.CRM006, "Unknown Opportunity stage value.", correlationId);
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                CrmError.Throw(CrmError.CRM007, "Opportunity name cannot be cleared while Open.", correlationId);
            }

            if (string.IsNullOrWhiteSpace(nextStep))
            {
                CrmError.Throw(CrmError.CRM007, "Opportunity next step cannot be cleared while Open.", correlationId);
            }

            if (!expectedCloseDate.HasValue)
            {
                CrmError.Throw(CrmError.CRM007, "Expected close date cannot be cleared while Open.", correlationId);
            }

            if (!nextFollowUpDate.HasValue)
            {
                CrmError.Throw(CrmError.CRM007, "Next follow-up date cannot be cleared while Open.", correlationId);
            }

            if (currentStage >= CrmSchema.Opportunity.StageValues.Develop)
            {
                if (string.IsNullOrWhiteSpace(need))
                {
                    CrmError.Throw(CrmError.CRM007, "Business need cannot be cleared once Opportunity reaches Develop.", correlationId);
                }

                if (string.IsNullOrWhiteSpace(decisionMaker))
                {
                    CrmError.Throw(CrmError.CRM007, "Decision maker cannot be cleared once Opportunity reaches Develop.", correlationId);
                }
            }

            if (currentStage >= CrmSchema.Opportunity.StageValues.Propose)
            {
                if (string.IsNullOrWhiteSpace(proposalSummary))
                {
                    CrmError.Throw(CrmError.CRM007, "Proposal summary cannot be cleared once Opportunity reaches Propose.", correlationId);
                }
            }
        }

        public static void ValidateCloseDateAndComments(
            DateTime? actualCloseDate,
            DateTime createdOnUtc,
            DateTime utcNow,
            string comments,
            Guid correlationId)
        {
            if (string.IsNullOrWhiteSpace(comments))
            {
                CrmError.Throw(CrmError.CRM014, "Close comments are required.", correlationId);
            }

            if (!actualCloseDate.HasValue)
            {
                CrmError.Throw(CrmError.CRM014, "Actual close date is required.", correlationId);
            }

            var normalizedCloseDate = UtcClock.NormalizeDateOnly(actualCloseDate.Value);
            var minDate = UtcClock.TodayUtc(createdOnUtc);
            var maxDate = UtcClock.TodayUtc(utcNow);

            if (normalizedCloseDate < minDate || normalizedCloseDate > maxDate)
            {
                CrmError.Throw(
                    CrmError.CRM014,
                    "Actual close date must be between the Opportunity creation UTC date and UTC today inclusive.",
                    correlationId);
            }
        }

        public static void ValidateWonEligibility(
            int stateCode,
            int statusCode,
            int currentStage,
            int lineCount,
            decimal total,
            DateTime? actualCloseDate,
            DateTime createdOnUtc,
            DateTime utcNow,
            string comments,
            Guid correlationId)
        {
            if (stateCode != CrmSchema.Opportunity.State.Active || statusCode != CrmSchema.Opportunity.Status.Open)
            {
                CrmError.Throw(CrmError.CRM014, "Only an Active Open Opportunity can be closed as Won.", correlationId);
            }

            if (currentStage != CrmSchema.Opportunity.StageValues.Close)
            {
                CrmError.Throw(CrmError.CRM014, "Opportunity must be in the Close stage to close as Won.", correlationId);
            }

            if (lineCount < 1)
            {
                CrmError.Throw(CrmError.CRM014, "At least one Opportunity Line is required to close as Won.", correlationId);
            }

            if (total <= 0m)
            {
                CrmError.Throw(CrmError.CRM014, "Opportunity Total must be greater than zero to close as Won.", correlationId);
            }

            ValidateCloseDateAndComments(actualCloseDate, createdOnUtc, utcNow, comments, correlationId);
        }

        public static void ValidateLostEligibility(
            int stateCode,
            int statusCode,
            int? lossReason,
            DateTime? actualCloseDate,
            DateTime createdOnUtc,
            DateTime utcNow,
            string comments,
            Guid correlationId)
        {
            if (stateCode != CrmSchema.Opportunity.State.Active || statusCode != CrmSchema.Opportunity.Status.Open)
            {
                CrmError.Throw(CrmError.CRM014, "Only an Active Open Opportunity can be closed as Lost.", correlationId);
            }

            if (!lossReason.HasValue || !IsValidLossReason(lossReason.Value))
            {
                CrmError.Throw(
                    CrmError.CRM014,
                    "Loss reason must be Price, Timing, Competitor, No Budget, No Decision, or Other.",
                    correlationId);
            }

            ValidateCloseDateAndComments(actualCloseDate, createdOnUtc, utcNow, comments, correlationId);
        }

        public static DealRiskAssessment EvaluateDealRisk(
            bool isOpen,
            DateTime? expectedCloseDate,
            DateTime? lastProgressOnUtc,
            DateTime? nextFollowUpDate,
            DateTime utcAsOf,
            CrmPolicyConfig config)
        {
            if (!isOpen)
            {
                return new DealRiskAssessment(DealHealthStatus.Closed, false, false, false);
            }

            if (config == null
                || !expectedCloseDate.HasValue
                || !lastProgressOnUtc.HasValue
                || !nextFollowUpDate.HasValue)
            {
                return new DealRiskAssessment(DealHealthStatus.DataUnavailable, false, false, false);
            }

            var asOfUtc = UtcClock.EnsureUtc(utcAsOf);
            var utcToday = UtcClock.TodayUtc(asOfUtc);
            var normalizedExpectedClose = UtcClock.NormalizeDateOnly(expectedCloseDate.Value);
            var normalizedNextFollowUp = UtcClock.NormalizeDateOnly(nextFollowUpDate.Value);
            var normalizedLastProgress = UtcClock.EnsureUtc(lastProgressOnUtc.Value);

            bool isExpectedCloseOverdue = normalizedExpectedClose < utcToday;
            bool isStaleProgress = (asOfUtc - normalizedLastProgress) > TimeSpan.FromDays(config.StaleDays);
            bool isFollowUpOverdue = normalizedNextFollowUp < utcToday;

            var status = (isExpectedCloseOverdue || isStaleProgress || isFollowUpOverdue)
                ? DealHealthStatus.AtRisk
                : DealHealthStatus.Healthy;

            return new DealRiskAssessment(
                status,
                isExpectedCloseOverdue,
                isStaleProgress,
                isFollowUpOverdue);
        }

        private static bool IsValidLossReason(int value)
        {
            return value == CrmSchema.Opportunity.LossReasonValues.Price
                || value == CrmSchema.Opportunity.LossReasonValues.Timing
                || value == CrmSchema.Opportunity.LossReasonValues.Competitor
                || value == CrmSchema.Opportunity.LossReasonValues.NoBudget
                || value == CrmSchema.Opportunity.LossReasonValues.NoDecision
                || value == CrmSchema.Opportunity.LossReasonValues.Other;
        }
    }
}
