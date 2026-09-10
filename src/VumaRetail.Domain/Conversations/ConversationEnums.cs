#pragma warning disable CS1591
namespace VumaRetail.Domain.Conversations;

/// <summary>The channels supported by conversational commerce.</summary>
public enum ConversationChannel { WhatsApp, Email }

/// <summary>The deterministic conversation states.</summary>
public enum ConversationState { Idle, Verifying, Collecting, Confirming, Submitting, Done, Escalated }

/// <summary>The only intents the assistant is permitted to handle.</summary>
public enum ConversationIntent { PlaceOrder, OrderStatus, RequestStatement, RequestInvoiceCopy, RequestPod, RequestCreditNote, Unknown }

/// <summary>Whether a contact binding has completed tenant-controlled verification.</summary>
public enum BindingVerificationState { Unverified, Verified, Locked, Revoked }

/// <summary>Consent for outbound conversation messages.</summary>
public enum ConversationConsentState { NotAsked, Granted, Withdrawn }

/// <summary>Direction of an immutable conversation turn.</summary>
public enum ConversationTurnDirection { Inbound, Outbound }
