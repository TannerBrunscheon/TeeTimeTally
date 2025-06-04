<script setup lang="ts">
import { RouterLink, RouterView } from 'vue-router'
import { useAuthenticationStore } from '@/stores/authentication'
import { Permissions } from '@/models' // Assuming Permissions are correctly defined and imported

const authenticationStore = useAuthenticationStore()

function login() {
  authenticationStore.login()
}

function register() {
  authenticationStore.register()
}

function logout() {
  authenticationStore.logout()
}
</script>

<template>
  <header>
    <nav class="navbar navbar-expand-lg bg-body-tertiary" aria-label="Main navigation">
      <div class="container-fluid">
        <RouterLink class="navbar-brand" :to="{ name: 'home' }">
          TeeTimeTally
        </RouterLink>
        <button
          class="navbar-toggler"
          type="button"
          data-bs-toggle="collapse"
          data-bs-target="#navbarNav"
          aria-controls="navbarNav"
          aria-expanded="false"
          aria-label="Toggle navigation"
        >
          <span class="navbar-toggler-icon"></span>
        </button>
        <div class="collapse navbar-collapse" id="navbarNav">
          <ul class="navbar-nav me-auto mb-2 mb-lg-0">
            <li class="nav-item">
              <RouterLink class="nav-link" :to="{ name: 'home' }" active-class="active">Home</RouterLink>
            </li>
            <li class="nav-item" v-if="authenticationStore.isAuthenticated && authenticationStore.hasPermission(Permissions.ReadCourses)">
              <RouterLink
                class="nav-link"
                :to="{ name: 'courses-index' }"
                active-class="active"
                >Courses</RouterLink
              >
            </li>

            <li class="nav-item" v-if="authenticationStore.isAuthenticated && authenticationStore.hasPermission(Permissions.ReadCourses)">
              <RouterLink
                class="nav-link"
                :to="{ name: 'groups-index' }"
                active-class="active"
                >Groups</RouterLink
              >
            </li>
            </ul>
          <div v-if="authenticationStore.isLoading" class="d-flex align-items-center text-muted me-3">
            <div class="spinner-border spinner-border-sm" role="status">
              <span class="visually-hidden">Loading...</span>
            </div>
          </div>
          <template v-else-if="authenticationStore.isAuthenticated && authenticationStore.user">
            <ul class="navbar-nav ms-auto">
              <li class="nav-item dropdown">
                <a class="nav-link dropdown-toggle" href="#" id="navbarDropdownUser" role="button" data-bs-toggle="dropdown" aria-expanded="false">
                  <img v-if="authenticationStore.user.profileImage" :src="authenticationStore.user.profileImage" alt="User" class="rounded-circle me-1" style="width: 24px; height: 24px; object-fit: cover;">
                  <span v-else class="d-inline-block rounded-circle bg-secondary text-white me-1" style="width: 24px; height: 24px; line-height: 24px; text-align: center; font-size: 0.8rem;">
                    {{ authenticationStore.user.fullName ? authenticationStore.user.fullName.charAt(0).toUpperCase() : '?' }}
                  </span>
                  {{ authenticationStore.user.fullName }}
                </a>
                <ul class="dropdown-menu dropdown-menu-end" aria-labelledby="navbarDropdownUser">
                  <li>
                    <RouterLink :to="{ name: 'authentication-profile' }" class="dropdown-item">Profile</RouterLink>
                  </li>
                  <li><hr class="dropdown-divider"></li>
                  <li>
                    <a class="dropdown-item" href="javascript:void(0)" @click="logout">Logout</a>
                  </li>
                </ul>
              </li>
            </ul>
          </template>
          <template v-else>
            <ul class="navbar-nav ms-auto">
              <li class="nav-item">
                <a class="nav-link" href="javascript:void(0)" @click="login">Login</a>
              </li>
              <li class="nav-item">
                <a class="nav-link" href="javascript:void(0)" @click="register">Register</a>
              </li>
            </ul>
          </template>
        </div>
      </div>
    </nav>
  </header>

  <main class="container mt-4 flex-shrink-0">
    <RouterView />
  </main>

  <footer class="footer mt-auto py-3 bg-light">
    <div class="container text-center">
      <span class="text-muted">&copy; {{ new Date().getFullYear() }} TeeTimeTally</span>
    </div>
  </footer>
</template>

<style scoped>
/* Ensure the main content area takes up available space */
#app { /* If your root element has id="app" */
  display: flex;
  flex-direction: column;
  min-height: 100vh;
}
main {
  flex-grow: 1;
}
.footer {
  font-size: 0.9em;
}
/* Optional: improve active link styling if Bootstrap defaults aren't enough */
.navbar-nav .nav-link.active {
  font-weight: bold;
  /* color: #0d6efd; */ /* Or your primary color */
}
</style>
