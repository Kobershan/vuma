package com.vuma.retail.mobile

/** Durable adapter for the offline intent contract; all claims are authenticated-session scoped. */
class MobileActionStore(private val dao: MobileCacheDao) {
    suspend fun enqueue(action: PendingAction, nowEpochMillis: Long): PendingAction {
        require(action.id.isNotBlank() && action.tenantId.isNotBlank() && action.userId.isNotBlank() && action.companyId.isNotBlank()) {
            "Pending action identity and scope are required"
        }
        dao.insertAction(action.toEntity(nowEpochMillis))
        return action.copy(state = PendingActionState.Queued)
    }

    suspend fun claimNext(session: TenantSession): PendingAction? {
        if (session.companyIds.isEmpty()) return null
        val entity = dao.nextAction(session.profile.tenantId, session.userId, session.companyIds.toList()) ?: return null
        check(dao.transition(entity.id, PendingActionState.Queued.name, PendingActionState.Sending.name) == 1) {
            "Pending action was claimed by another worker"
        }
        return entity.toModel(PendingActionState.Sending)
    }

    suspend fun markAccepted(id: String, nowEpochMillis: Long) = transition(id, PendingActionState.Accepted, null, nowEpochMillis)

    suspend fun markRejected(id: String, reason: String, nowEpochMillis: Long) = transition(id, PendingActionState.Rejected, reason, nowEpochMillis)

    suspend fun retry(id: String, nowEpochMillis: Long) = transition(id, PendingActionState.Queued, null, nowEpochMillis)

    suspend fun requireReauthentication(id: String, nowEpochMillis: Long) = transition(id, PendingActionState.RequiresReauthentication, null, nowEpochMillis)

    private suspend fun transition(id: String, target: PendingActionState, error: String?, nowEpochMillis: Long): PendingAction {
        val existing = dao.action(id) ?: error("Pending action was not found")
        val expected = when (target) {
            PendingActionState.Queued -> PendingActionState.Rejected
            else -> PendingActionState.Sending
        }
        check(dao.transitionWithMetadata(id, expected.name, target.name, if (target == PendingActionState.Queued) 1 else 0, error, nowEpochMillis) == 1) {
            "Invalid pending action transition"
        }
        return existing.toModel(target)
    }

    private fun PendingAction.toEntity(now: Long) = PendingActionEntity(
        id, tenantId, userId, companyId, operation, payloadJson, PendingActionState.Queued.name,
        updatedAtEpochMillis = now,
    )

    private fun PendingActionEntity.toModel(state: PendingActionState) = PendingAction(
        id, tenantId, userId, companyId, operation, payloadJson, state,
    )
}
