import type {
  ComponentDebugSnapshot,
  ComponentGraphDebugSnapshot,
  ComponentValueDebugSnapshot,
  EntityDebugSnapshot,
} from './types';

export type ComponentMutationExecutor = (
  entity: EntityDebugSnapshot,
  component: ComponentDebugSnapshot,
  value: ComponentValueDebugSnapshot,
) => Promise<void>;

export function getComponentGraph(component: ComponentDebugSnapshot): ComponentGraphDebugSnapshot {
  const graph = (component as ComponentDebugSnapshot & { graph?: ComponentGraphDebugSnapshot }).graph;
  return {
    id: graph?.id ?? `${component.type.assemblyName}:${component.type.fullName}`,
    parentId: graph?.parentId ?? null,
    parentType: graph?.parentType ?? null,
    group: graph?.group ?? null,
    order: graph?.order ?? 0,
  };
}

export function countComponentGroups(components: ComponentDebugSnapshot[]) {
  return new Set(components.map((component) => getComponentGraph(component).group ?? 'Components')).size;
}
