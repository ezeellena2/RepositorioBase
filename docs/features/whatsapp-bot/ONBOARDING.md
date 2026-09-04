# WhatsApp Bot — Provisioning Runbook

**Status:** Proposed. Operational reference for [ADR-005](../../decisions/ADR-005-Adopt-WhatsApp-Delivery-Channel.md).

Two paths. The platform-owned channel is provisioned by hand, once. An organization-owned channel is provisioned by the organization through Embedded Signup, and is roadmap work (WA-011).

## Path A — the platform-owned channel

Nine steps in the Meta dashboard. The output is the eight credentials the Platform panel stores under WA-REQ-002.

1. **Business portfolio.** Create or reuse one at `business.facebook.com` with real legal name, site, and contact. Immediately start **business verification** under Security Centre — it takes days to weeks and gates both scaling here and all of Path B.
2. **Meta app.** At `developers.facebook.com`, create a **Business**-type app linked to that portfolio. Record **App ID** and **App Secret** from Settings → Basic.
3. **WhatsApp product.** Add it to the app. Meta creates a WABA and a test number. Record the **WABA ID**.
4. **Real number.** WhatsApp → API Setup → Add phone number. Verify by SMS or call. Record the **Phone Number ID** and the **display number**. The number must have no active WhatsApp or WhatsApp Business App account; delete it from the handset first.
5. **Register with a PIN.** `POST /{phone-number-id}/register` with `messaging_product` and a six-digit PIN. Record the **PIN**; it is required to migrate the number later.
6. **System user token.** Business settings → Users → System users. Create an administrator system user, assign the WABA with full control, generate a token for the app with `whatsapp_business_messaging` and `whatsapp_business_management`, expiry **Never**. Record the **Access Token**. The token shown in the Getting Started panel expires in 24 hours and must never reach production (WA-REQ-003).
7. **Webhook.** Generate a random **Verify Token**. Publish the receiver first — Meta issues the challenge request the moment you save. Then subscribe to `messages` and `account_update`.
8. **Load into the panel.** Enter the eight values; the three secrets are stored as encrypted envelopes. Run the connectivity check.
9. **Verification and limits.** Unverified businesses stay in trial limits. Verified accounts start in an initial tier that scales with volume and quality. A response-only assistant inside the service window rarely reaches these limits.

### The eight credentials

| Credential | Source | Purpose |
|---|---|---|
| App ID | App → Settings → Basic | identifies the app |
| App Secret · secret | App → Settings → Basic | verifies the webhook signature (WA-REQ-015) |
| WABA ID | WhatsApp → API Setup | templates, subscription, inbound routing (WA-REQ-005) |
| Phone Number ID | WhatsApp → API Setup | the send target; the number itself never appears in an API call |
| Display number | idem | panel display and `wa.me` links |
| Access Token · secret | Business settings → System users | authenticates every Graph API call |
| Verify Token · secret | invented locally | the webhook challenge (WA-REQ-014) |
| Two-step PIN | set at registration | number recovery and migration |

### Several product lines

Do not repeat the process. One WABA supports several numbers: add one per product at step 4. App ID, App Secret, Access Token, and Verify Token are shared; only the Phone Number ID differs, which is one additional channel row.

## Path B — an organization-owned channel

### Enablement, once, before the feature can be sold

1. Business verification approved — the same one as Path A.
2. **Tech Provider** status for the app.
3. **App Review** for advanced access to `whatsapp_business_management`, `whatsapp_business_messaging`, and `business_management`. Requires a screencast of the flow and a published privacy policy.
4. A **Facebook Login for Business** configuration of the Embedded Signup type; record the configuration identifier.
5. The Facebook JavaScript SDK loaded in the React client.

### Per organization, in the product

The organization signs in with Facebook, selects or creates its business portfolio, adds and verifies its number, and accepts the permissions. It copies nothing.

### Backend, automatically

1. Exchange the returned code for an access token, server-side only.
2. `POST /{waba-id}/subscribed_apps` — **without this no webhook ever arrives**, and the channel still looks healthy.
3. `POST /{phone-number-id}/register` with a generated PIN.
4. `GET /{phone-number-id}` for display number and quality.
5. Persist the channel with `Mode = TenantOwned`.
6. Prompt the organization to add a payment method in Meta; without one the channel stops after the free allowance.

### Credential mapping

Of the eight credentials, App ID, App Secret, and Verify Token are the platform's and do not change. WABA ID, Phone Number ID, and Access Token arrive with the signup. Display number and PIN are resolved by the backend.

## Ownership and billing

The organization owns its WABA and number, sees them in its own Business Manager, and may revoke platform access at any time — subscribe to `account_update` to detect it and suspend the channel (WA-REQ-006). Meta bills the organization directly. A response-only assistant stays inside the service window, where user-initiated conversations carry no per-message charge; the platform's variable cost is the language model, not WhatsApp.

Tiers, template categories, and pricing change. Confirm current values against Meta's documentation before making commercial commitments.
