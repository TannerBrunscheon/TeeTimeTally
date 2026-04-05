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
    // Skins Net: show skins winnings only (do not subtract buy-ins)
    return (p.skinsWinnings ?? 0);
  }

  function displayedNetPerRound(p: any) {
    const times = p.timesPlayed ?? 0;
    if (times === 0) return 0;
    return displayedNet(p) / times;
  }

  const sortedPlayers = computed(() => {
    if (!report.value) return [] as GroupYearEndReportResponse['players'];
    const arr = [...report.value.players];
    const dir = sortDir.value === 'asc' ? 1 : -1;
    arr.sort((a: any, b: any) => {
      switch (sortBy.value) {
        case 'fullName': return a.fullName.localeCompare(b.fullName) * dir;
        case 'timesPlayed': return (a.timesPlayed - b.timesPlayed) * dir;
        case 'wins': return ((a.wins ?? 0) - (b.wins ?? 0)) * dir;
        case 'cth': return ((a.closestToHoleCount ?? 0) - (b.closestToHoleCount ?? 0)) * dir;
  case 'winnings': return ((a.totalWinnings ?? 0) - (b.totalWinnings ?? 0)) * dir;
  case 'net': return (displayedNet(a) - displayedNet(b)) * dir;
  case 'netPerRound': return (displayedNetPerRound(a) - displayedNetPerRound(b)) * dir;
        case 'avg': return ((a.avgVsParPerRound ?? Number.MAX_SAFE_INTEGER) - (b.avgVsParPerRound ?? Number.MAX_SAFE_INTEGER)) * dir;
        case 'median': return ((a.medianVsParPerRound ?? Number.MAX_SAFE_INTEGER) - (b.medianVsParPerRound ?? Number.MAX_SAFE_INTEGER)) * dir;
        default: return 0;
      }
    });
    return arr;
  });

  return { sortBy, sortDir, toggleSort, displayedNet, sortedPlayers };
}
