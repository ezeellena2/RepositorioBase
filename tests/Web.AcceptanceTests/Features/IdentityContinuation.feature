@IdentityContinuation
Feature: Identity continuation journeys
    What Tasks 19-25 added, driven through the browser a person would use rather than asserted at the request
    level alone: a personal account of one's own, the devices a person holds, and the two ways a password moves.

Scenario: A newcomer sets up a personal account and their document is never shown in full
    Given a visitor sets up a personal account
    Then setting up answers neutrally without revealing whether the address was taken
    When they open the delivered confirmation link and confirm
    And they sign in with the password they hold
    Then their profile shows the document masked and never the number they submitted

Scenario: An identity that already has an organization adds a personal context
    Given a confirmed identity that belongs to one organization
    When they sign in with the password they hold
    And they add a personal context to the identity they already have
    Then both the organization and their personal context are offered to them

Scenario: A person ends another device from the list of the devices they hold
    Given a confirmed identity signed in on two devices
    When they end the other device from their device list
    Then the other device is sent back to sign in
    And their own device is still signed in

Scenario: A forgotten password is reset from the delivered link and then changed from inside
    Given a confirmed identity that cannot remember its password
    When they ask for a reset link and follow the one delivered
    Then the password they had no longer signs them in
    And the one they chose does
    When they change their password from inside
    Then the password they just replaced no longer signs them in
