@Home
Feature: Home

Scenario: The header offers the two ways into the product
    Given a user visits the home page
    Then the header offers the link "Log in"
    And the header offers the link "Register"

Scenario: A visitor is offered no product navigation and no page content
    Given a user visits the home page
    Then the product navigation is not offered
    And the page carries no content
