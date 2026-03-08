import { useHttpClient } from '@/composables/useHttpClient';
import type { GroupYearEndReportDto } from '@/types/reports';

export async function fetchGroupYearEndReport(groupId: string, year: number): Promise<GroupYearEndReportDto> {
  const { data } = await useHttpClient().get<GroupYearEndReportDto>(`/api/groups/${groupId}/reports/year/${year}`);
  return data;
}
