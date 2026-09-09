@PlatformOperations
Feature: Platform operations
    The Platform ceremony and panel, driven through the browser a person would use.

    A deployment is bootstrapped once, so the ceremony is walked once per run and every scenario reads what that
    one walk observed. Nothing is seeded and nothing is confirmed in the database: the invitation and the
    confirmation are read out of the mail that was delivered, and each gate is answered on the screen that offers
    it.

Scenario: The first owner reaches the panel through every gate, and through no fewer
    Given the first owner has been walked in from the deployment's cold start
    Then the cold start had left one Platform tenant, one pending invitation for the configured address and no membership
    And no visitor had been offered the panel
    And a failed delivery had been resent without naming anybody, leaving one invitation pending
    And the invitation had arrived, and choosing a password had granted no membership
    And answering the invitation again had reissued the confirmation, and only the newest link had confirmed the address
    And signing in had granted no membership either, and the panel was still refused
    And the invitation link they still held had carried them into the second factor
    And only the second factor had granted the membership
    And the Platform panel is offered to them

Scenario: An owner suspends and reactivates an organization
    Given the first owner has been walked in from the deployment's cold start
    And an active organization exists
    When they step up and suspend that organization
    Then the organization is suspended with the reason they gave
    And the suspension appears in the Platform audit
    When they reactivate it
    Then the organization is active again

Scenario: The panel offers no prohibited capability
    Given the first owner has been walked in from the deployment's cold start
    Then the panel offers no impersonation, deletion or context override
    And the owner is the one offered the administrator invitation

Scenario: A later administrator is invited from the panel and walks the same gates
    Given the first owner has been walked in from the deployment's cold start
    When the owner invites another administrator
    And that administrator answers the delivered invitation, confirms, signs in and proves a second factor
    Then Platform holds two memberships and the panel is offered to the new administrator

Scenario: An identity that already exists is invited to Platform and keeps the password it had
    Given the first owner has been walked in from the deployment's cold start
    And an identity that already has an account of its own
    When the owner invites that identity to Platform
    And it answers the invitation with a different password
    Then the password it already had is the one that still signs it in

# The account is chosen from what the directory lists rather than seeded for the occasion: the screen renders one
# page of it, ordered by identity and with nothing to search by, so an account written for this scenario is not
# necessarily one an operator could reach. Everything the scenario states is read off the row.
Scenario: An owner stops one account from the identities directory and lifts the suspension again
    Given the first owner has been walked in from the deployment's cold start
    And an account the identities directory lists
    When they step up and open the identities directory
    Then that account is listed with the status it holds
    When they suspend it for a reason from the closed set
    Then the directory shows it administratively suspended
    When they lift the suspension with the acknowledgement left unticked
    Then the directory shows it back in the status it held before
    And the retention policy is offered on the same visit

# Last on purpose. One browser serves the whole feature and the walk above is cached, so signing out here ends
# the session every earlier scenario operates from; reordering this would strand them with no failure that says so.
Scenario: A returning owner proves the second factor again before the directories open
    Given the first owner has been walked in from the deployment's cold start
    When they sign out and sign in again with their password
    Then the panel asks for the second factor and shows no directory
    When they prove the second factor on the panel
    Then the Platform panel is offered to them
