/**
 * Composable for standardizing the display of status badges and text.
 */
export function useStatusBadges() {
  /**
   * Returns the appropriate Bootstrap badge class based on the round status.
   * @param status The status string of the round.
   * @returns A string of CSS classes for the badge.
   */
  const getStatusBadgeClass = (status: string): string => {
    switch (status) {
      case 'Finalized':
        return 'badge bg-success';
      case 'Completed':
        return 'badge bg-info text-dark';
      case 'InProgress':
        return 'badge bg-primary';
      case 'SetupComplete':
        return 'badge bg-secondary';
      case 'PendingSetup':
        return 'badge bg-warning text-dark';
      default:
        return 'badge bg-light text-dark';
    }
  };

  /**
   * Returns a human-readable string for a given status key.
   * @param status The status string of the round.
   * @returns A formatted string for display.
   */
  const formatStatusText = (status: string): string => {
    const statusMap: { [key: string]: string } = {
      'PendingSetup': 'Pending Setup',
      'SetupComplete': 'Setup Complete',
      'InProgress': 'In Progress',
      'Completed': 'Completed (Awaiting Finalization)',
      'Finalized': 'Finalized',
    };
    return statusMap[status] || status;
  };

  return { getStatusBadgeClass, formatStatusText };
}
