@IdentityAccess
Feature: Identity access journeys
    Every journey the identity foundation promises, driven through the browser a person would use.

Scenario: A pending registration becomes usable only after confirmation
    Given a visitor registers an organization
    Then the registration answers neutrally without revealing whether the address was taken
    And the organization is not usable before its confirmation
    When the invitee confirms the address
    Then they can sign in and reach their access page

Scenario: An identity that belongs to nothing is signed in with no active organization
    Given a confirmed identity with no membership
    When they sign in
    Then their access page shows no active organization

Scenario: An identity with one membership operates inside it
    Given a confirmed identity with one active membership
    When they sign in
    Then their access page shows that organization as active

Scenario: An identity with several memberships chooses between them
    Given a confirmed identity with two active memberships
    When they sign in
    And they select the second organization
    Then their access page shows the second organization as active

Scenario: Permissions do not cross organizations
    Given a confirmed identity that may invite in one organization only
    When they sign in
    And they select the organization where they may not invite
    Then the invite action is not offered

Scenario: A newcomer joins through an invitation exactly once
    Given a member invites a newcomer
    When the newcomer registers from the invitation
    And the newcomer confirms the address
    And the newcomer signs in and accepts the invitation
    Then they hold one membership in the inviting organization
    When the newcomer accepts the same invitation again
    Then they still hold one membership

Scenario: An identity that already exists accepts an invitation without registering again
    Given a confirmed identity is invited to an organization
    When they sign in and accept the invitation
    Then they hold one membership in the inviting organization

Scenario: A revoked session stops authenticating
    Given a confirmed identity with one active membership
    When they sign in
    And their session is revoked
    Then the next protected page sends them back to sign in

Scenario: Sign-in throttling is per account and recovers
    Given a confirmed identity with one active membership
    When the account is locked out by repeated failures
    Then a correct password is still refused
    And an unrelated account can still sign in

Scenario: The served contract matches what the client calls
    Then every identity route the client calls is declared in the served OpenAPI document
    And no legacy identity route is served
