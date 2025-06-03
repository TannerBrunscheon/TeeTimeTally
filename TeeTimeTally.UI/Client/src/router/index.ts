import { createRouter, createWebHistory } from 'vue-router'
import HomeView from '../views/HomeView.vue'
import { useAuthenticationStore } from '@/stores/authentication'
import { Permissions } from '@/models'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/',
      name: 'home',
      component: HomeView
    },
    {
      path: '/courses',
      name: 'courses',
      children: [
        {
          path: '',
          name: 'courses-index',
          component: () => import('../views/Courses/IndexView.vue'),
          meta: { requiresPermission: Permissions.ManageRoundCth }
        }
      ]
    },
    {
      path: '/authentication',
      name: 'authentication',
      children: [
        {
          path: 'profile',
          name: 'authentication-profile',
          component: () => import('../views/Authentication/ProfileView.vue'),
          meta: { requiresAuth: true }
        },
      ]
    },
    {
      path: '/rounds',
      name: 'round-management',
      children: [
        {
          path: 'new', // Or your preferred path
          name: 'create-round',
          component: () => import('../views/Rounds/CreateRoundView.vue'), // Create this component
          meta: { requiresAuth: true, requiresPermission: Permissions.CreateRounds }
        },
        {
          path: ':roundId/overview', // Or similar
          name: 'round-overview',
          component: () => import('../views/Rounds/RoundOverviewView.vue'), // Create this component
          props: true, // To pass roundId as a prop
          meta: { requiresAuth: true, requiresPermission: Permissions.ReadGroupRounds }
        }
      ]
    },
    {
      path: '/groups',
      name: 'groups',
      children: [
        {
          path: '',
          name: 'groups-index',
          component: () => import('../views/Groups/GroupsIndexView.vue'),
          meta: { requiresAuth: true, requiredPermission: Permissions.ReadGroups }
        },
        {
          path: 'create',
          name: 'create-group',
          component: () => import('../views/Groups/CreateGroupView.vue'), // This component needs to be created
          meta: { requiresAuth: true, requiredPermission: Permissions.CreateGroups }
        },
        {
          path: ':groupId',
          name: 'group-detail',
          component: () => import('../views/Groups/GroupDetailView.vue'),
          props: true, // Allows groupId to be passed as a prop
          meta: { requiresAuth: true, requiredPermission: Permissions.ReadGroups }
        }
      ]
    },
  ]
})

router.beforeResolve(async (to, from, next) => {
  const authenticationStore = useAuthenticationStore()

  if (
    to.meta.requiresPermission &&
    !authenticationStore.hasPermission(to.meta.requiresPermission as string)
  ) {
    return next({ name: 'home' })
  } else if (to.meta.requiresAuth && !authenticationStore.isAuthenticated) {
    return next({ name: 'authentication-login', query: { redirect: to.fullPath } })
  } else if (to.meta.requiresGuest && authenticationStore.isAuthenticated) {
    return next({ name: 'home' })
  } else {
    return next()
  }
})

export default router
