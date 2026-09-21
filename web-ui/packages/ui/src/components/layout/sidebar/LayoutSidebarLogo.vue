<script setup lang="ts">
interface Props {
  /** Logo size */
  size?: 'sm' | 'md' | 'lg'
  /** Show text beside logo */
  showText?: boolean
  /** Layout direction */
  direction?: 'horizontal' | 'vertical'
  /** Logo text (shown when showText is true) */
  text?: string
  /** Subtitle text */
  subtitle?: string
}

withDefaults(defineProps<Props>(), {
  size: 'md',
  showText: false,
  direction: 'horizontal',
  text: 'TabTab Admin',
  subtitle: 'Admin',
})

const sizeClasses = {
  sm: {
    container: 'h-9 w-9',
    scale: 0.9,
  },
  md: {
    container: 'h-10 w-10',
    scale: 1,
  },
  lg: {
    container: 'h-12 w-12',
    scale: 1.2,
  },
}
</script>

<template>
  <div :class="['flex items-center', direction === 'horizontal' ? 'gap-2' : 'flex-col gap-1']">
    <div
      :class="[
        'flex items-center justify-center rounded-xl',
        'bg-gradient-to-br from-primary to-primary/80',
        'text-primary-foreground font-bold',
        'shadow-lg shadow-primary/25',
        'transition-all duration-300 ease-out',
        'hover:scale-105 hover:shadow-xl hover:shadow-primary/30',
        'cursor-pointer',
        sizeClasses[size].container,
      ]"
    >
      <svg
        viewBox="0 0 40 40"
        fill="none"
        xmlns="http://www.w3.org/2000/svg"
        class="w-full h-full p-1"
        :style="{ transform: `scale(${sizeClasses[size].scale})` }"
      >
        <rect
          x="2"
          y="2"
          width="36"
          height="36"
          rx="10"
          fill="white"
          class="fill-primary-foreground"
        />
        <!-- 眼睛 -->
        <circle cx="13" cy="18" r="5" fill="#3b82f6" class="fill-primary" />
        <circle cx="27" cy="18" r="5" fill="#3b82f6" class="fill-primary" />
        <circle cx="13" cy="18" r="2" fill="#1a1a1a" />
        <circle cx="27" cy="18" r="2" fill="#1a1a1a" />
        <!-- 鼻子 -->
        <circle cx="20" cy="22" r="2" fill="#3b82f6" class="fill-primary" />
        <!-- 嘴巴 -->
        <path
          d="M16 27c2 1.5 4 1.5 6 0"
          stroke="#3b82f6"
          stroke-width="1.5"
          stroke-linecap="round"
          class="stroke-primary"
        />
        <!-- 龙角 -->
        <path
          d="M12 10c-1-2-3-2-3-2s0 3 3 3c1 0 2-0.5 2-1"
          stroke="#3b82f6"
          stroke-width="1.5"
          stroke-linecap="round"
          fill="none"
          class="stroke-primary"
        />
        <path
          d="M28 10c1-2 3-2 3-2s0 3-3 3c-1 0-2-0.5-2-1"
          stroke="#3b82f6"
          stroke-width="1.5"
          stroke-linecap="round"
          fill="none"
          class="stroke-primary"
        />
        <!-- 头顶圆点 -->
        <circle cx="20" cy="10" r="1.5" fill="#3b82f6" class="fill-primary" />
      </svg>
    </div>
    <div
      v-if="showText"
      :class="['flex flex-col min-w-0', direction === 'vertical' && 'items-center text-center']"
    >
      <span class="text-sm font-semibold tracking-tight">{{ text }}</span>
      <span class="text-xs text-muted-foreground">{{ subtitle }}</span>
    </div>
  </div>
</template>
