using System;
using System.Threading.Tasks;

namespace Vuma.Domain.Interfaces
{
    public interface ISagaCoordinator
    {
        /// <summary>
        /// Records an immutable intent for the entire operation before dispatching any legs.
        /// </summary>
        /// <param name="intentId">The unique identifier for the intent.</param>
        /// <param name="operation">The operation details.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task RecordIntent(Guid intentId, object operation);

        /// <summary>
        /// Dispatches a leg of the operation to a specific company database.
        /// </summary>
        /// <param name="intentId">The intent identifier.</param>
        /// <param name="legId">The leg identifier.</param>
        /// <param name="companyId">The target company identifier.</param>
        /// <param name="legDetails">The leg details.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task DispatchLeg(Guid intentId, Guid legId, Guid companyId, object legDetails);

        /// <summary>
        /// Compensates for a failed leg by creating a new document (never deleting or editing existing).
        /// </summary>
        /// <param name="intentId">The intent identifier.</param>
        /// <param name="legId">The leg identifier.</param>
        /// <param name="compensationDetails">The compensation details.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task CompensateLeg(Guid intentId, Guid legId, object compensationDetails);

        /// <summary>
        /// Reports on in-flight intents, unapplied legs, and their statuses.
        /// </summary>
        /// <returns>A task representing the asynchronous report.</returns>
        Task<string> ReportIntents();

        /// <summary>
        /// Alarms on timed-out intents.
        /// </summary>
        /// <param name="timeoutThreshold">The timeout threshold.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task AlarmOnTimeout(TimeSpan timeoutThreshold);
    }
}