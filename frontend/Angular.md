# Angular Best Practices

**Modern Angular patterns for building maintainable, performant, and accessible applications.** This guide covers standalone components, signals, modern control flow, SSR, Tailwind-only styling, and declarative state management.

This project uses **Angular 21** which is **zoneless by default** — no Zone.js dependency. Change detection is signal-based. Do NOT add `provideZoneChangeDetection()` to app config.

---

## Component Architecture

**Always use standalone components** (default since Angular 16+). Do NOT set `standalone: true` — it's the default.

**Every component must use:**
- `changeDetection: ChangeDetectionStrategy.OnPush`
- `styles: []` (Tailwind-only, no custom CSS)
- `input()`, `output()`, `model()` functions (not decorators)
- Modern control flow (`@if`, `@for`, `@switch`, `@let`)
- Host bindings via `host` object (not `@HostBinding`/`@HostListener`)

```typescript
@Component({
  selector: 'app-user-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: [],
  host: {
    '[class.active]': 'isActive()',
    '(click)': 'handleClick($event)',
  },
  template: `
    @if (user(); as userData) {
      <div class="text-gray-900 font-medium">{{ userData.name }}</div>
    }
  `,
})
export class UserCardComponent {
  user = input<User>();
  isActive = input<boolean>(false);
  userSelected = output<User>();
}
```

---

## Inputs, Outputs, and Models

Use the function-based API for all component communication:

```typescript
export class UserFormComponent {
  // Required input
  userId = input.required<string>();

  // Optional input with default
  isActive = input<boolean>(false);

  // Model for two-way binding
  userName = model<string>('');

  // Output event
  userSaved = output<User>();

  // Computed derived state
  displayName = computed(() => this.userName() || 'Anonymous User');
}
```

**Input transforms** for attribute binding:

```typescript
export class MyComponent {
  isActive = input(false, { transform: booleanAttribute });
  maxItems = input(10, { transform: numberAttribute });
}
```

---

## Modern Control Flow

Use `@if`, `@for`, `@switch`, and `@let` — never `*ngIf`, `*ngFor`, or `*ngSwitch`.

```typescript
// ✅ Modern control flow
@if (users().length > 0) {
  @let filtered = getFilteredUsers();
  @for (user of filtered; track user.id) {
    <div [class.active]="user.isActive">{{ user.name }}</div>
  }
} @else {
  <div>No users found</div>
}

// ❌ Legacy directives — do NOT use
<div *ngIf="users().length > 0">
  <div *ngFor="let user of users(); trackBy: trackUser">
```

**Always use `track` in `@for` blocks** for performance.

Do NOT use `ngClass` or `ngStyle` — use `[class]` and `[style]` bindings:

```html
<div [class.active]="user.isActive" [style.background-color]="user.color">
```

---

## Signals and State Management

**All state must be signal-based.** No traditional properties for reactive data.

```typescript
export class UserListComponent {
  private userService = inject(UserService);
  private route = inject(ActivatedRoute);

  // Route parameter
  private userId = this.route.snapshot.paramMap.get('id');

  // Observable → signal (declarative, no manual subscription)
  user = toSignal(
    this.userId
      ? this.userService.getUser(this.userId).pipe(
          catchError((err) => {
            console.error('Error loading user:', err);
            return of(null);
          })
        )
      : of(null),
    { initialValue: null }
  );

  // Derived state
  loading = computed(() => this.user() === undefined);
  isActive = computed(() => this.user()?.status === 'active');

  // Local state
  searchTerm = signal<string>('');

  displayName = computed(() => {
    const user = this.user();
    return user ? `${user.firstName} ${user.lastName}` : 'Unknown User';
  });
}
```

### Key Principles

1. **No OnInit/OnDestroy** — use declarative initialization with `toSignal()`
2. **No manual `.subscribe()`** — `toSignal()` handles lifecycle automatically
3. **No manual state updates** — let signals handle reactivity
4. **Pure `computed()` functions** — no side effects
5. **Single source of truth** — each piece of state has one signal owner

---

## No Lifecycle Hooks

**NEVER implement `OnInit`, `OnDestroy`, or `OnChanges`.** Use declarative patterns:

```typescript
// ✅ Declarative — no lifecycle hooks
export class MyComponent {
  private apiService = inject(ApiService);
  private route = inject(ActivatedRoute);

  private itemId = this.route.snapshot.paramMap.get('id');

  item = toSignal(
    this.itemId ? this.apiService.getItem(this.itemId) : of(null),
    { initialValue: null }
  );

  isLoaded = computed(() => this.item() !== undefined);

  // DOM manipulation only when needed
  constructor() {
    afterNextRender(() => {
      document.querySelector('input')?.focus();
    });
  }
}

// ❌ Imperative — do NOT use
export class MyComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<void>();
  ngOnInit() { /* subscription management */ }
  ngOnDestroy() { this.destroy$.next(); }
}
```

---

## RxJS Integration

**Always convert observables to signals with `toSignal()`.** Never manually subscribe.

```typescript
export class DataComponent {
  private apiService = inject(ApiService);
  private route = inject(ActivatedRoute);

  data = toSignal(
    this.route.params.pipe(
      switchMap((params) => this.apiService.getData(params['id'])),
      catchError(() => of([]))
    ),
    { initialValue: [] }
  );

  hasData = computed(() => this.data().length > 0);
}
```

---

## Service Layer

- Use `providedIn: 'root'` for singleton services
- Use `inject()` function instead of constructor injection
- Centralize API calls in the service layer

```typescript
@Injectable({ providedIn: 'root' })
export class UserService {
  private http = inject(HttpClient);

  getUsers(): Observable<User[]> {
    return this.http.get<User[]>(`${environment.apiUrl}/users`);
  }
}
```

---

## Routing and Lazy Loading

Lazy load all feature routes:

```typescript
export const routes: Routes = [
  {
    path: 'dashboard',
    loadComponent: () => import('./pages/dashboard/dashboard'),
  },
  {
    path: 'users',
    loadChildren: () => import('./pages/users/users.routes'),
  },
];
```

---

## SSR Considerations

### Platform-Specific Code

Guard browser-only APIs:

```typescript
import { isPlatformBrowser } from '@angular/common';
import { PLATFORM_ID, inject } from '@angular/core';

export class MyComponent {
  private platformId = inject(PLATFORM_ID);

  doSomething() {
    if (isPlatformBrowser(this.platformId)) {
      // Safe to use window, document, localStorage
    }
  }
}
```

### SSR Route Configuration

In `app.routes.server.ts`:

```typescript
export const serverRoutes: ServerRoute[] = [
  { path: '', renderMode: RenderMode.Prerender },       // Static pages
  { path: 'about', renderMode: RenderMode.Prerender },
  { path: '**', renderMode: RenderMode.Server },         // Dynamic pages
];
```

### Avoid in SSR Context

- Direct `window`, `document`, `localStorage` access without platform check
- Browser-only third-party libraries without guards
- CSS-in-JS libraries that depend on the DOM

---

## Tailwind CSS Only

**No custom CSS.** All styling via Tailwind utility classes.

```typescript
@Component({
  styles: [], // Always empty
  template: `
    <div class="flex items-center gap-4 p-6 bg-white rounded-lg shadow-sm">
      <h2 class="text-xl font-semibold text-gray-900">{{ title() }}</h2>
      <button class="px-4 py-2 bg-blue-600 text-white rounded-md
                     hover:bg-blue-700 transition-colors duration-200">
        Save
      </button>
    </div>
  `,
})
```

Common patterns:

```html
<!-- Responsive layout -->
<div class="hidden lg:flex">

<!-- Hover effects with group -->
<div class="group hover:bg-blue-100">
  <span class="group-hover:text-blue-600">Child</span>
</div>

<!-- Gradients -->
<div class="bg-gradient-to-r from-blue-500 to-indigo-500">

<!-- Opacity -->
<div class="bg-white/10">

<!-- Transitions -->
<div class="transition-all duration-200 hover:scale-105">
```

---

## Forms

Prefer **reactive forms** with typed form controls:

```typescript
export class UserFormComponent {
  private fb = inject(FormBuilder);

  form = this.fb.group({
    name: ['', Validators.required],
    email: ['', [Validators.required, Validators.email]],
  });
}
```

---

## Content Projection

Use `<ng-content>` with `select` for flexible component composition:

```typescript
@Component({
  selector: 'app-card',
  template: `
    <div class="rounded-lg shadow-sm bg-white">
      <div class="p-4 border-b">
        <ng-content select="[slot=header]" />
      </div>
      <div class="p-4">
        <ng-content />
      </div>
    </div>
  `,
})
export class CardComponent {}
```

---

## Images

Use `NgOptimizedImage` for all static images:

```typescript
import { NgOptimizedImage } from '@angular/common';

@Component({
  imports: [NgOptimizedImage],
  template: `<img ngSrc="/logo.png" width="200" height="50" priority />`,
})
```

Note: `NgOptimizedImage` does not work for inline base64 images.

---

## Event Handling

Name handlers by action, not by event:

```typescript
@Component({
  template: `
    <button (click)="saveUser()">Save</button>
    <input (keydown.enter)="searchItems()" (keydown.escape)="clearSearch()" />
  `,
})
export class UserFormComponent {
  saveUser() { /* ... */ }
  searchItems() { /* ... */ }
  clearSearch() { /* ... */ }
}
```

---

## Naming Conventions

| Type | Convention | Example |
|------|-----------|---------|
| Component selectors | kebab-case | `app-user-profile` |
| Components | PascalCase | `UserProfileComponent` |
| Services | PascalCase + `.service.ts` | `user.service.ts` |
| Models/Interfaces | PascalCase + `.model.ts` | `user.model.ts` |
| Enums | PascalCase + `.enum.ts` | `status.enum.ts` |
| Variables | camelCase | `userName` |
| Constants | UPPER_SNAKE_CASE | `MAX_RETRIES` |
| Event handlers | Action-based | `saveUser()` not `onClick()` |

---

## Testing

```bash
npm test
```

- Use Vitest (Angular 21 default) for unit and component tests
- Follow the AAA pattern (Arrange, Act, Assert)
- Use `TestBed` for component integration tests
- Mock services with Angular testing utilities
- Test component behavior, not implementation details

---

## Performance

- **Zoneless by default** — Angular 21 uses signal-based change detection, no Zone.js
- **OnPush change detection** on all components (defense-in-depth)
- **`track` in all `@for` loops**
- **Lazy load routes** for code splitting
- **Debounce user inputs** for search/filter
- **Virtual scrolling** for large lists (`@angular/cdk/scrolling`)
- **`NgOptimizedImage`** for static images
- **SSR with hydration** for fast initial load

---

## Common Pitfalls

- `@else if (expr; as x)` does not work — use separate `@if` blocks
- `computed()` must be pure — no side effects
- `NgOptimizedImage` does not support base64 images
- Always check `isPlatformBrowser()` before using `window`/`document`/`localStorage`
- SSR hydration mismatch: ensure server and client render identical content
