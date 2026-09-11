@Localization
Feature: Localization
  A visitor can choose Spanish and keep that choice when signing in and opening another page.

  Scenario: Spanish survives sign-in and full-page navigation
    Given a confirmed identity opens the anonymous localization shell
    When the visitor chooses Español and signs in through the Spanish form
    Then the Spanish access page survives a full-page navigation without raw catalog keys
