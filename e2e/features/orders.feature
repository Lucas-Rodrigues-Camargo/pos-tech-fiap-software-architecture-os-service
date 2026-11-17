Feature: Distributed service order saga
  Scenario: Approved and paid order reaches completion
    Given the three services and their isolated databases are running
    When a customer opens an order and the mechanic diagnoses it
    And the customer approves and pays the budget
    And the mechanic completes the repair
    Then the order is finished across the services

  Scenario: Rejected budget compensates the saga
    Given the three services and their isolated databases are running
    When a customer opens an order and the mechanic diagnoses it
    And the customer rejects the budget
    Then execution and billing are canceled before the order is canceled

  Scenario: Paid order is refunded before cancellation completes
    Given the three services and their isolated databases are running
    When a customer opens an order and the mechanic diagnoses it
    And the customer approves and pays the budget
    And the customer cancels before the repair starts
    Then the payment is refunded before the order is canceled
