@Localization
Feature: Localization
  A visitor can choose any supported target language and keep that choice when signing in and opening another page.

  @DataSource:../../../src/Web/ClientApp/src/i18n/languages.json
  @DataSet:journeys
  Scenario: A supported target language survives sign-in and full-page navigation
    Given a confirmed identity opens the anonymous localization shell
    When the visitor chooses <language> and signs in through the localized form
    Then the localized access page survives a full-page navigation without raw catalog keys
