Feature: Moving stock through a warehouse
    As a warehouse operator
    I want the rules about stock lines enforced by the warehouse that holds them
    So that no route into the system can leave it holding something impossible

Background:
    Given an active warehouse "OSL-01"

Scenario: Stock is received
    When 12 of "PAL-1001" are received
    Then the warehouse holds 1 stock line
    And "PAL-1001" shows a quantity of 12

Scenario: A SKU the warehouse already holds is refused
    Given the warehouse already holds 5 of "PAL-1001"
    When 3 of "PAL-1001" are received
    Then the stock is refused because the SKU is already held
    And the warehouse holds 1 stock line
    And "PAL-1001" shows a quantity of 5

Scenario: The same SKU in a different letter case is still the same SKU
    Given the warehouse already holds 5 of "PAL-1001"
    When 3 of "pal-1001" are received
    Then the stock is refused because the SKU is already held

Scenario: A deactivated warehouse takes no new stock
    Given the warehouse has been deactivated
    When 4 of "PAL-2002" are received
    Then the stock is refused because the warehouse is not active
    And the warehouse holds no stock lines

Scenario: A SKU is free again once its line has been removed
    Given the warehouse already holds 5 of "PAL-1001"
    When the line for "PAL-1001" is removed
    And 8 of "PAL-1001" are received
    Then the warehouse holds 1 stock line
    And "PAL-1001" shows a quantity of 8

Scenario: A line can be depleted to nothing without disappearing
    Given the warehouse already holds 5 of "PAL-1001"
    When the quantity of "PAL-1001" is changed to 0
    Then the warehouse holds 1 stock line
    And "PAL-1001" shows a quantity of 0

Scenario Outline: Stock cannot go negative
    Given the warehouse already holds 5 of "PAL-1001"
    When the quantity of "PAL-1001" is changed to <quantity>
    Then the change is refused because a quantity cannot be negative
    And "PAL-1001" shows a quantity of 5

    Examples:
      | quantity |
      | -1       |
      | -100     |

Scenario: Receiving a negative quantity is refused outright
    When -1 of "PAL-3003" are received
    Then the change is refused because a quantity cannot be negative
    And the warehouse holds no stock lines

Scenario: Changing a line the warehouse does not hold
    Given the warehouse already holds 5 of "PAL-1001"
    When the quantity of a line held by no warehouse is changed to 7
    Then the warehouse reports that it holds no such line

Scenario: Removing a line the warehouse does not hold
    When a line held by no warehouse is removed
    Then the warehouse reports that it holds no such line
