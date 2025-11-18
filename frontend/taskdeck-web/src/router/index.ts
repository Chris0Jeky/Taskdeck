import { createRouter, createWebHistory } from 'vue-router'
import BoardsListView from '../views/BoardsListView.vue'
import BoardView from '../views/BoardView.vue'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/',
      redirect: '/boards',
    },
    {
      path: '/boards',
      name: 'boards',
      component: BoardsListView,
    },
    {
      path: '/boards/:id',
      name: 'board',
      component: BoardView,
      props: true,
    },
  ],
})

export default router
