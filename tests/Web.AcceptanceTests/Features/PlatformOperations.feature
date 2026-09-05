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

# Last on purpose. One browser serves the whole feature and the walk above is cached, so signing out here ends
# the session every earlier scenario operates from; reordering this would strand them with no failure that says so.
Scenario: A returning owner proves the second factor again before the directories open
    Given the first owner has been walked in from the deployment's cold start
    When they sign out and sign in again with their password
    Then the panel asks for the second factor and shows no directory
    When they prove the second factor on the panel
    Then the Platform panel is offered to them
