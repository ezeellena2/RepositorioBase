@PlatformOperations
Feature: Platform operations
    The Platform ceremony and panel, driven through the browser a person would use.

Scenario: A deployment starts with one pending Platform owner and nobody who can act
    Given the application has started with a configured Platform owner
    Then exactly one Platform tenant and one pending owner invitation exist
    And no Platform membership exists
    And the Platform panel is not offered to a visitor

Scenario: The owner invitation can be resent without naming anybody
    Given the application has started with a configured Platform owner
    And the owner invitation could not be delivered
    When anyone asks for it to be resent
    Then the answer says nothing about who it was for
    And exactly one Platform owner invitation is still pending

Scenario: An administrator reaches Platform only after every gate
    Given a Platform administrator has been invited
    When they register with the invitation and a password they chose
    Then they hold no Platform membership yet
    When they confirm their address and sign in
    Then they hold no Platform membership yet
    And the Platform panel is not offered to them
    When they complete the second factor and acknowledge their recovery codes
    Then the Platform panel is offered to them

Scenario: An administrator suspends and reactivates an organization
    Given a Platform administrator has completed every gate
    And an active organization exists
    When they step up and suspend that organization
    Then the organization is suspended with the reason they gave
    And the suspension appears in the Platform audit
    When they reactivate it
    Then the organization is active again

Scenario: The panel offers no prohibited capability
    Given a Platform administrator has completed every gate
    Then the panel offers no impersonation, deletion or context override
    And an administrator cannot invite another administrator
