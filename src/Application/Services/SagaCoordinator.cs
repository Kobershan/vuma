using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Vuma.Domain.Interfaces;

namespace Vuma.Application.Services
{
    public class SagaCoordinator : ISagaCoordinator
    {
        private readonly Dictionary<Guid, object> _intents = new Dictionary<Guid, object>();
        private readonly Dictionary<(Guid, Guid), object> _legs = new Dictionary<(Guid, Guid), object>();

        public async Task RecordIntent(Guid intentId, object operation)
        {
            if (_intents.ContainsKey(intentId))
                throw new InvalidOperationException("Intent already exists.");

            _intents.Add(intentId, operation);
            await Task.CompletedTask;
        }

        public async Task DispatchLeg(Guid intentId, Guid legId, Guid companyId, object legDetails)
        {
            if (!_intents.ContainsKey(intentId))
                throw new InvalidOperationException("Intent not found.");

            var legKey = (intentId, legId);
            if (_legs.ContainsKey(legKey))
                return; // Idempotent: skip if already dispatched

            _legs.Add(legKey, legDetails);
            await Task.CompletedTask;
            // TODO: Implement actual dispatch to company database
        }

        public async Task CompensateLeg(Guid intentId, Guid legId, object compensationDetails)
        {
            var legKey = (intentId, legId);
            if (!_legs.ContainsKey(legKey))
                throw new InvalidOperationException("Leg not found.");

            // Compensation is a new document, never an edit or delete.
            // TODO: Implement compensation logic
            await Task.CompletedTask;
        }

        public async Task<string> ReportIntents()
        {
            var report = "In-flight Intents:\n";
            foreach (var intent in _intents)
            {
                report += $"- Intent ID: {intent.Key}\n";
            }
            return await Task.FromResult(report);
        }

        public async Task AlarmOnTimeout(TimeSpan timeoutThreshold)
        {
            // TODO: Monitor intents and alarm if timeout exceeded
            await Task.CompletedTask;
        }
    }
}