# CHATBOT — the conversational assistant on WhatsApp and email

> **The requirement, in the operator's words.** "A chat bot that can take orders from customers, or
> request statements or PODs or credit notes, and will WhatsApp or email them."

This document is the contract for Stage **22b**. It is referenced by Stages 07 (statements), 10c
(invoices), 14 (orders), 14b (approval and pro formas), 19 (customer identity and consent), 22
(messaging transport), 24 (proof of delivery) and 30b (metering), and by ADR-129 – ADR-133.

---

## 1. What it is, and what it deliberately is not

**It is** a narrow, deterministic assistant for existing customers of a tenant, reachable on WhatsApp
and email, that can do six things and nothing else:

| Intent | What happens |
|---|---|
| `PlaceOrder` | Builds an order from the customer's own catalogue and prices, then submits it as a **pro forma awaiting approval** (ADR-131) |
| `OrderStatus` | Reports the state of an order the requester's account owns |
| `RequestStatement` | Sends the account statement for one company, for a period |
| `RequestInvoiceCopy` | Sends a copy of an invoice the requester's account owns |
| `RequestPod` | Sends the proof of delivery for a delivery to that account |
| `RequestCreditNote` | Logs a credit request against a specific invoice and its lines, routed to approval; sends the credit note once issued |

**It is not** a salesperson, a negotiator, a support agent or a general assistant. It does not quote
prices it computed, discuss stock it inferred, agree to terms, or answer questions outside the six
intents. Anything else it hands to a human with a clear message saying so.

**The model's job is small and bounded** (ADR-129): classify the intent, extract entities (item, quantity,
invoice number, date range, company), and phrase a reply from a supplied result. **Every number,
document, price, quantity, balance and date comes from the API.** The model is never given the ability
to compute a figure, and a reply containing a figure that does not appear verbatim in the API result is
a defect the stage's tests must catch.

---

## 2. Identity — the part that must not be got wrong

A statement is a list of what somebody owes. A POD names an address and who signed. These leave the
building over a public channel, so identity comes first and is not negotiable (ADR-130).

```
Inbound message from +27 82 555 0134
  1. Match the sender to registry.contact_bindings → contact → customer account(s) → company(ies)
  2. No binding?           → onboarding flow, never data. "Ask your account manager to link this number."
  3. Binding found?        → is it VERIFIED and unexpired?
  4. Sensitive intent?     → statement, POD, invoice copy, credit note: require a fresh OTP
                             (default: OTP valid 10 minutes, re-required after 24 hours of inactivity)
  5. Scope every query to  → that contact's accounts, in those companies, and nothing else
```

- **A binding is created by the tenant**, from inside the product, against a specific contact of a
  specific customer in a specific company — never by the requester claiming an identity.
- **One phone number may reach several companies** where the contact exists in each and the companies
  are linked with `SharedReporting` (`TRADING_GROUP.md` §2). Otherwise the bot asks which company, and
  answers for exactly one.
- **A document is delivered as a short-lived signed link, not as an unauthenticated attachment**, unless
  the tenant configures otherwise per channel. Default expiry 24 hours, single-tenant scoped, revocable,
  and every fetch is audited.
- **Rate limits and lockout**: per sender, per intent, per hour. Repeated failed verification locks the
  binding and notifies the tenant.
- **POPIA** (`docs/SECURITY.md`): consent recorded per channel per contact, withdrawable in one message
  (`STOP`), and withdrawal is honoured immediately and permanently until re-granted.
- **Nothing about another customer ever leaves.** The scoping is structural — the query cannot express
  another account — not a filter that a handler might forget.

---

## 3. Conversation, deterministically

The bot is a **state machine**; the model only labels the edges (ADR-129).

```
IDLE ──intent───► VERIFYING ──ok──► COLLECTING ──complete──► CONFIRMING ──yes──► SUBMITTING ──► DONE
  ▲                   │ fail            │ missing entity          │ no                    │ error
  └───────────────────┴─────────────────┴─────────────────────────┴───────────────────────┘
```

- Every state has an explicit timeout and an explicit escalation to a human.
- **Every action that creates or sends something is confirmed first**, quoting back exactly what the API
  returned: the lines, the quantities, the company, the total, and that it needs approval.
- A conversation carries a `conversation_id` and every submission carries an **idempotency key**, so a
  duplicate WhatsApp delivery cannot place two orders.
- The customer can always type `AGENT`, `HELP`, `STOP` or `CANCEL`, in any state, and each does exactly
  what it says.
- Transcripts are retained per the tenant's retention policy and are visible in the CRM (Stage 19)
  against the contact.

---

## 4. Ordering through the bot

- The catalogue, prices and availability are the customer's own — their price list, their promotions,
  the linked companies they trade with (`MULTI_COMPANY.md` §4). **The bot reads group availability with
  its `AsAt` and never commits from it** (ADR-119).
- Items are resolved by barcode, code, or name search; an ambiguous name returns choices and never
  guesses.
- **A submitted order becomes a `ProFormaOrder` requiring approval** (Stage 14b, ADR-131), which is
  exactly the rep flow with a different capture channel. A tenant may allow-list a customer for
  straight-through orders; that is a per-customer setting with an audit trail, off by default.
- A mixed-company order splits into one document per company at approval, exactly as every other channel
  does (ADR-102). The customer is told which companies will invoice them, before they confirm.
- Availability shown is stamped and hedged: "as at 09:41 — this is not reserved until we approve it."

---

## 5. Delivery — WhatsApp and email

**The transport is Stage 22's**, not this stage's (ADR-132). This stage owns conversation state, intent
handling and document assembly; Stage 22 owns the WhatsApp Business Cloud API session, the email
sender, templates, opt-out and deliverability.

| Channel | Notes |
|---|---|
| **WhatsApp** | Business Cloud API. Outside the 24-hour service window, only approved template messages may open a conversation — every proactive message this stage sends must map to a registered template, and the stage ships the template set it needs. Documents go as a link by default, as a PDF document message where the tenant enables it. |
| **Email** | The tenant's configured sender, SPF/DKIM aligned. PDF attached or linked per tenant policy. Inbound email is parsed for intent the same way a WhatsApp message is. |

- Every outbound message records: contact, channel, template, document reference, and who or what
  triggered it.
- Delivery failures retry with backoff and then surface to a human queue. A statement that failed to
  send is not silently dropped.

---

## 6. Documents the bot can send

| Document | Comes from | Scoping rule |
|---|---|---|
| Statement | Stage 07 AR, per company | One company per statement. A combined pack across linked companies is marked **not a statement of account for any single entity** |
| Invoice copy | Stage 10c | Only invoices belonging to the requester's own account, in a company they are bound to |
| Credit note | Stage 10 / 14b | Only once issued and approved. The request itself creates nothing financial |
| POD | Stage 24 | Only deliveries to that account. Signature images are included only where the tenant has enabled it |
| Order confirmation | Stage 14 / 14b | Marked clearly as awaiting approval where it is |

Every document is generated by the module that owns it — QuestPDF through Stage 29's reporting path.
**The bot never renders a financial document itself.**

---

## 7. Failure, honestly

- **The model is unavailable** → the bot falls back to a keyword menu (`1 Statement, 2 Order, 3 POD…`)
  and keeps working. It never guesses an intent it could not classify.
- **The API is unavailable** → the bot says so and does not improvise a figure. There is no cached
  "probably still true" balance.
- **A figure would have to be computed** → refuse and escalate. There is no arithmetic in this stage.
- **The requester asks something outside the six intents** → say what it can do, offer a human.
- **Anything ambiguous about identity** → stop. Identity failures never degrade gracefully.

---

## 8. What a reviewer checks

`conversation-safety` (`.claude/agents/conversation-safety.md`) reviews against exactly these:

1. No figure, price, balance, quantity or date in an outbound message that is not verbatim from an API
   result. Prove it by the assembly path, not by reading a happy-path example.
2. No document leaves without a verified binding, a fresh OTP for sensitive intents, and scoping to the
   requester's own accounts and companies — structurally, not by a filter.
3. The model cannot reach a data source directly. It receives a classification prompt and a result to
   phrase; it never holds a database connection, a repository or a raw query.
4. Prompt-injected content in an inbound message cannot change scope, identity or intent authority.
   Message text is data, everywhere, and the tests include an injection fixture.
5. Every submission is idempotent by conversation and idempotency key; a duplicate delivery cannot place
   two orders or send two credit requests.
6. `STOP` is honoured immediately and permanently; consent state is checked before every outbound
   message, not only at opt-in.
7. Rate limits and verification lockout exist and are tested.
8. A bot order lands as a pro forma requiring approval unless the customer is explicitly allow-listed,
   and the allow-listing is audited.
9. WhatsApp template rules are respected — no proactive free-form message outside the service window.
10. Transport is Stage 22's; this stage contains no second WhatsApp or SMTP client.
