Feature: A warehouse through its working life
    As someone running a depot network
    I want a warehouse's details and its availability to be changed independently
    So that a site can be taken out of use without losing what it holds or what it is called

Background:
    Given an active warehouse "OSL-01"

Scenario: A newly registered warehouse is open for business
    Then the warehouse is active
    And the warehouse has never been changed

Scenario: A warehouse is renamed
    When the warehouse is renamed to "Oslo Central"
    Then the warehouse is named "Oslo Central"
    And the warehouse records that it has changed

Scenario: A warehouse is moved
    When the warehouse is relocated to "Bergen"
    Then the warehouse is in "Bergen"
    And the warehouse records that it has changed

Scenario: A warehouse is resized
    When the capacity is changed to 400
    Then the capacity is 400
    And the warehouse records that it has changed

Scenario: A warehouse can be emptied of capacity but not given less than none
    When the capacity is changed to 0
    Then the capacity is 0

Scenario Outline: A negative capacity is refused
    When the capacity is changed to <capacity>
    Then the change is refused because a capacity cannot be negative
    And the capacity is 1200

    Examples:
      | capacity |
      | -1       |
      | -500     |

Scenario: A warehouse is taken out of use
    When the warehouse is deactivated
    Then the warehouse is not active
    And the warehouse records that it has changed

Scenario: A warehouse is brought back into use
    Given the warehouse has been deactivated
    When the warehouse is activated
    Then the warehouse is active

Scenario: Taking a warehouse out of use leaves the stock where it is
    Given the warehouse already holds 5 of "PAL-1001"
    When the warehouse is deactivated
    Then the warehouse holds 1 stock line
    And "PAL-1001" shows a quantity of 5

Scenario: Stock already held can still be moved in a deactivated warehouse
    Given the warehouse already holds 5 of "PAL-1001"
    And the warehouse has been deactivated
    When the quantity of "PAL-1001" is changed to 2
    Then "PAL-1001" shows a quantity of 2

Scenario: The code a warehouse trades under does not change with its name
    When the warehouse is renamed to "Somewhere else entirely"
    Then the warehouse still trades as "OSL-01"
