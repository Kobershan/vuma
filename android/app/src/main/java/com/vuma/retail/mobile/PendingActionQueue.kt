package com.vuma.retail.mobile

/**
 * In-memory representation of the mobile intent queue contract. The Android Room adapter persists
 * the same records; this state machine keeps retries explicit and prevents another tenant or user
 * from claiming an action.
 */
class PendingActionQueue(
    private val actions: MutableList<PendingAction> = mutableListOf(),
) {
    fun enqueue(action: PendingAction): PendingAction {
        require(action.id.isNotBlank()) { "Action id is required" }
        require(action.tenantId.isNotBlank()) { "Action tenant is required" }
        require(action.userId.isNotBlank()) { "Action user is required" }
        require(action.companyId.isNotBlank()) { "Action company is required" }
        require(actions.none { it.id == action.id }) { "Action id already exists" }
        actions += action.copy(state = PendingActionState.Queued)
        return action
    }

    /** Claims the oldest queued action belonging to this authenticated tenant/user pair. */
    fun claimNext(session: TenantSession): PendingAction? {
        val index = actions.indexOfFirst {
            it.state == PendingActionState.Queued &&
                it.tenantId == session.profile.tenantId &&
                it.userId == session.userId &&
                it.companyId in session.companyIds
        }
        if (index < 0) return null
        val claimed = actions[index].copy(state = PendingActionState.Sending)
        actions[index] = claimed
        return claimed
    }

    fun markAccepted(id: String) = transition(id, PendingActionState.Accepted, PendingActionState.Sending)

    fun markRejected(id: String) = transition(id, PendingActionState.Rejected, PendingActionState.Sending)

    fun retry(id: String): PendingAction = transition(id, PendingActionState.Queued, PendingActionState.Rejected)

    fun requireReauthentication(id: String): PendingAction =
        transition(id, PendingActionState.RequiresReauthentication, PendingActionState.Sending)

    fun find(id: String): PendingAction? = actions.firstOrNull { it.id == id }

    private fun transition(id: String, target: PendingActionState, expected: PendingActionState): PendingAction {
        val index = actions.indexOfFirst { it.id == id }
        require(index >= 0) { "Pending action was not found" }
        require(actions[index].state == expected) { "Invalid pending action transition" }
        val updated = actions[index].copy(state = target)
        actions[index] = updated
        return updated
    }
}
