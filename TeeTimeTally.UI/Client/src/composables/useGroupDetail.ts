import { ref, computed, nextTick } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { useGroupsStore } from '@/stores/groups';
import { useCoursesStore } from '@/stores/courses';
import type { GroupMember } from '@/models';

export function useGroupDetail(initialGroupId?: string) {
  const groupsStore = useGroupsStore();
  const courseStore = useCoursesStore();
  const route = useRoute();
  const router = useRouter();

  const groupIdRef = ref<string | string[] | undefined>(initialGroupId ?? route.params.groupId as string | string[]);
  const group = computed(() => groupsStore.currentGroup);
  const groupMembers = ref<GroupMember[]>([]);
  const defaultCourseName = ref<string>('N/A');
  const financialCardMode = ref<'view' | 'editForm'>('view');
  const showReportModal = ref(false);

  async function loadGroupData() {
    const gid = Array.isArray(groupIdRef.value) ? groupIdRef.value[0] : groupIdRef.value;
    if (!gid) {
      router.push({ name: 'groups-index' });
      return;
    }

    const groupResult = await groupsStore.fetchGroupById(gid);
    if (groupResult.isFailure) return;

    if (groupsStore.currentGroup?.defaultCourseId) {
      const courseResult = await courseStore.getCourseById(groupsStore.currentGroup.defaultCourseId);
      if (courseResult.isSuccess && courseResult.value) defaultCourseName.value = courseResult.value.name;
      else defaultCourseName.value = 'N/A (Error)';
    } else {
      defaultCourseName.value = 'N/A';
    }

    const membersResult = await groupsStore.fetchGroupMembers(gid);
    if (membersResult.isSuccess && membersResult.value) groupMembers.value = [...membersResult.value];
    else groupMembers.value = [];
  }

  async function refreshAndRestoreScroll(action?: () => Promise<void>) {
    const scrollY = window.scrollY;
    if (action) await action();
    await nextTick();
    window.scrollTo(0, scrollY);
  }

  async function handleGroupUpdated() {
    await refreshAndRestoreScroll(loadGroupData);
  }

  function handleEditFinancialsClicked() {
    financialCardMode.value = 'editForm';
  }

  async function handleFinancialsSuccessfullyUpdated() {
    await refreshAndRestoreScroll(loadGroupData);
    financialCardMode.value = 'view';
  }

  function handleCancelFinancialEdit() {
    financialCardMode.value = 'view';
  }

  async function handleMembersUpdated() {
    await refreshAndRestoreScroll(loadGroupData);
  }

  return {
    groupIdRef,
    group,
    groupMembers,
    defaultCourseName,
    financialCardMode,
    showReportModal,
    loadGroupData,
    handleGroupUpdated,
    handleEditFinancialsClicked,
    handleFinancialsSuccessfullyUpdated,
    handleCancelFinancialEdit,
    handleMembersUpdated,
  };
}
