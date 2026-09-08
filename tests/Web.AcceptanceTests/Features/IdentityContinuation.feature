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

Scenario: A custom role decides what a member may do, and taking the permission back takes the action away
    Given an organization with an administrator and a member who holds nothing
    When the administrator signs in and puts the invitation permission into a role of their own
    And they give that role to the member
    Then the member is offered the invitation action
    When the administrator takes the permission back out of the role
    Then the member is no longer offered it

Scenario: An owner hands the organization to a member
    Given an organization with an administrator and a member who holds nothing
    When the administrator signs in and hands the organization to the member
    Then the member holds the ownership and the administrator does not

Scenario: Resending an invitation leaves one usable link, and withdrawing it leaves none
    Given an organization with an administrator and a member who holds nothing
    And an identity that has been invited to it
    When the administrator resends the invitation
    Then the link that was replaced no longer accepts
    When the administrator withdraws the invitation
    Then the link that replaced it no longer accepts either
    And the invitee holds no membership

Scenario: A forgotten password is reset from the delivered link and then changed from inside
    Given a confirmed identity that cannot remember its password
    When they ask for a reset link and follow the one delivered
    Then the password they had no longer signs them in
    And the one they chose does
    When they change their password from inside
    Then the password they just replaced no longer signs them in
