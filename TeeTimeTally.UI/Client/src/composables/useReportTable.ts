import { ref, computed } from 'vue';
import type { Ref } from 'vue';
import type { GroupYearEndReportResponse } from '@/models/reports';

export function useReportTable(report: Ref<GroupYearEndReportResponse | null>, group: Ref<any | null>) {
  const sortBy = ref<string>('timesPlayed');
  const sortDir = ref<'asc' | 'desc'>('desc');

  function toggleSort(key: string) {
    if (sortBy.value === key) {
      sortDir.value = sortDir.value === 'asc' ? 'desc' : 'asc';
    } else {
      sortBy.value = key;
      sortDir.value = 'desc';
    }
  }

  function displayedNet(p: any) {
    const buyIn = group.value?.activeFinancialConfiguration?.buyInAmount ?? 0;
    const contributed = (p.timesPlayed ?? 0) * buyIn;
    const net = (p.netWinnings ?? 0) - contributed;
    return net;
  }

  const sortedPlayers = computed(() => {
    if (!report.value) return [] as GroupYearEndReportResponse['players'];
    const arr = [...report.value.players];
    const dir = sortDir.value === 'asc' ? 1 : -1;
    arr.sort((a: any, b: any) => {
      switch (sortBy.value) {
        case 'fullName': return a.fullName.localeCompare(b.fullName) * dir;
        case 'timesPlayed': return (a.timesPlayed - b.timesPlayed) * dir;
        case 'cth': return ((a.closestToHoleCount ?? 0) - (b.closestToHoleCount ?? 0)) * dir;
        case 'winnings': return ((a.netWinnings ?? 0) - (b.netWinnings ?? 0)) * dir;
        case 'net': return (displayedNet(a) - displayedNet(b)) * dir;
        case 'avg': return ((a.avgVsParPerRound ?? Number.MAX_SAFE_INTEGER) - (b.avgVsParPerRound ?? Number.MAX_SAFE_INTEGER)) * dir;
        case 'median': return ((a.medianVsParPerRound ?? Number.MAX_SAFE_INTEGER) - (b.medianVsParPerRound ?? Number.MAX_SAFE_INTEGER)) * dir;
        default: return 0;
      }
    });
    return arr;
  });

  return { sortBy, sortDir, toggleSort, displayedNet, sortedPlayers };
}
