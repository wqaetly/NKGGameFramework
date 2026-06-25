import { memo, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  Background,
  Controls,
  Handle,
  MarkerType,
  Position,
  ReactFlow,
  type ReactFlowInstance,
  type Edge,
  type Node,
  type NodeProps,
  type NodeTypes,
} from '@xyflow/react';
import '@xyflow/react/dist/style.css';
import { getComponentGraph, type ComponentMutationExecutor } from './componentGraphModel';
import type {
  ComponentDebugSnapshot,
  ComponentValueDebugNode,
  ComponentValueDebugSnapshot,
  EntityDebugSnapshot,
} from './types';

type ComponentNodeData = {
  component: ComponentDebugSnapshot;
  onInspectComponent: ComponentSelectionHandler;
  searchMatch: boolean;
  searchActive: boolean;
};

type ComponentSelectionHandler = (component: ComponentDebugSnapshot) => void;

type ComponentGroupNodeData = {
  label: string;
  count: number;
};

type ComponentFlowNode = Node<ComponentNodeData, 'componentNode'>;
type ComponentGroupFlowNode = Node<ComponentGroupNodeData, 'componentGroup'>;
type ComponentGraphFlowNode = ComponentFlowNode | ComponentGroupFlowNode;

function isComponentFlowNode(node: ComponentGraphFlowNode): node is ComponentFlowNode {
  return node.type === 'componentNode';
}

type ComponentTreeNode = {
  component: ComponentDebugSnapshot;
  children: ComponentTreeNode[];
};

type StructuredNodeChange = (path: string, nextNode: ComponentValueDebugNode) => void;

const COMPONENT_NODE_WIDTH = 360;
const COMPONENT_GROUP_NODE_WIDTH = 126;
const COMPONENT_GROUP_NODE_HEIGHT = 28;

const componentNodeTypes = {
  componentNode: memo(ComponentNode),
  componentGroup: memo(ComponentGroupNode),
} satisfies NodeTypes;

export function ComponentGraphCanvas({
  entity,
  query,
  onInspectComponent,
  selectedComponentKey,
}: {
  entity: EntityDebugSnapshot;
  query: string;
  onInspectComponent: ComponentSelectionHandler;
  selectedComponentKey?: string | null;
}) {
  const stableComponents = useStableComponents(entity.components);
  const normalizedQuery = query.trim();
  const searchMatches = useMemo(
    () => findMatchingComponentIds(stableComponents, normalizedQuery),
    [stableComponents, normalizedQuery],
  );
  const filteredComponents = useMemo(
    () => stableComponents,
    [stableComponents],
  );
  const rawGraph = useMemo(
    () => buildComponentGraph(
      filteredComponents,
      onInspectComponent,
      selectedComponentKey ?? null,
      searchMatches,
    ),
    [filteredComponents, onInspectComponent, selectedComponentKey, searchMatches],
  );
  const graph = useStableGraphElements(rawGraph);
  const reactFlowRef = useRef<ReactFlowInstance<ComponentGraphFlowNode, Edge> | null>(null);
  const searchFocusNode = useMemo<ComponentFlowNode | null>(
    () => normalizedQuery
      ? graph.nodes.find((node): node is ComponentFlowNode => isComponentFlowNode(node) && node.data.searchMatch) ?? null
      : null,
    [graph.nodes, normalizedQuery],
  );

  useEffect(() => {
    if (!reactFlowRef.current || !searchFocusNode || !normalizedQuery) {
      return;
    }

    const width = searchFocusNode.width ?? COMPONENT_NODE_WIDTH;
    const height = searchFocusNode.height ?? estimateComponentNodeHeight(searchFocusNode.data.component);
    reactFlowRef.current.setCenter(
      searchFocusNode.position.x + width / 2,
      searchFocusNode.position.y + height / 2,
      { zoom: 1.08, duration: 240 },
    );
  }, [normalizedQuery, searchFocusNode?.id]);

  return (
    <div className="component-flow">
      {graph.nodes.length ? (
        <ReactFlow
          onInit={(instance) => {
            reactFlowRef.current = instance;
          }}
          nodes={graph.nodes}
          edges={graph.edges}
          nodeTypes={componentNodeTypes}
          defaultEdgeOptions={{
            type: 'smoothstep',
            markerEnd: { type: MarkerType.ArrowClosed, color: '#d17a22' },
          }}
          fitView
          fitViewOptions={{ padding: 0.18 }}
          minZoom={0.35}
          maxZoom={1.25}
          nodesDraggable={false}
          nodesConnectable={false}
          elementsSelectable
          panOnScroll
          zoomOnPinch
          proOptions={{ hideAttribution: true }}
        >
          <Background color="#2f3135" gap={18} size={1} />
          <Controls showInteractive={false} />
        </ReactFlow>
      ) : (
        <div className="component-flow-empty">No matching components</div>
      )}
    </div>
  );
}

function useStableComponents(components: ComponentDebugSnapshot[]) {
  const previousByIdRef = useRef<Map<string, ComponentDebugSnapshot>>(new Map());
  const previousComponentsRef = useRef<ComponentDebugSnapshot[]>([]);

  return useMemo(() => {
    const previousById = previousByIdRef.current;
    const previousComponents = previousComponentsRef.current;
    const nextById = new Map<string, ComponentDebugSnapshot>();
    const nextComponents = components.map((component) => {
      const graph = getComponentGraph(component);
      const previous = previousById.get(graph.id);
      const stable = previous ? stabilizeComponentSnapshot(previous, component) : component;
      nextById.set(graph.id, stable);
      return stable;
    });
    const stableComponents = areReferenceArraysEqual(nextComponents, previousComponents)
      ? previousComponents
      : nextComponents;

    previousByIdRef.current = nextById;
    previousComponentsRef.current = stableComponents;
    return stableComponents;
  }, [components]);
}

function useStableGraphElements(graph: {
  nodes: ComponentGraphFlowNode[];
  edges: Edge[];
}) {
  const previousRef = useRef<{
    nodesById: Map<string, ComponentGraphFlowNode>;
    edgesById: Map<string, Edge>;
    nodes: ComponentGraphFlowNode[];
    edges: Edge[];
    result: { nodes: ComponentGraphFlowNode[]; edges: Edge[] };
  }>({
    nodesById: new Map(),
    edgesById: new Map(),
    nodes: [],
    edges: [],
    result: { nodes: [], edges: [] },
  });

  return useMemo(() => {
    const previous = previousRef.current;
    const nodesById = new Map<string, ComponentGraphFlowNode>();
    const edgesById = new Map<string, Edge>();
    const nodes = graph.nodes.map((node) => {
      const previousNode = previous.nodesById.get(node.id);
      const stableNode = previousNode && areFlowNodesEqual(previousNode, node)
        ? previousNode
        : node;
      nodesById.set(stableNode.id, stableNode);
      return stableNode;
    });
    const edges = graph.edges.map((edge) => {
      const previousEdge = previous.edgesById.get(edge.id);
      const stableEdge = previousEdge && areFlowEdgesEqual(previousEdge, edge)
        ? previousEdge
        : edge;
      edgesById.set(stableEdge.id, stableEdge);
      return stableEdge;
    });
    const stableNodes = areReferenceArraysEqual(nodes, previous.nodes)
      ? previous.nodes
      : nodes;
    const stableEdges = areReferenceArraysEqual(edges, previous.edges)
      ? previous.edges
      : edges;

    if (stableNodes === previous.nodes && stableEdges === previous.edges) {
      previousRef.current = {
        nodesById,
        edgesById,
        nodes: previous.nodes,
        edges: previous.edges,
        result: previous.result,
      };
      return previous.result;
    }

    const result = { nodes: stableNodes, edges: stableEdges };
    previousRef.current = { nodesById, edgesById, nodes: stableNodes, edges: stableEdges, result };
    return result;
  }, [graph]);
}

function buildComponentGraph(
  components: ComponentDebugSnapshot[],
  onInspectComponent: ComponentSelectionHandler,
  selectedComponentKey: string | null,
  searchMatches: Set<string>,
) {
  const componentById = new Map<string, ComponentTreeNode>();

  for (const component of components) {
    const graph = getComponentGraph(component);
    componentById.set(graph.id, { component, children: [] });
  }

  const roots: ComponentTreeNode[] = [];
  for (const node of componentById.values()) {
    const graph = getComponentGraph(node.component);
    const parent = graph.parentId ? componentById.get(graph.parentId) : null;
    if (parent && parent !== node) {
      parent.children.push(node);
    } else {
      roots.push(node);
    }
  }

  const sortNodes = (nodes: ComponentTreeNode[]) => {
    nodes.sort(compareComponentNodes);
    for (const node of nodes) {
      sortNodes(node.children);
    }
  };
  sortNodes(roots);

  const groups = groupComponentRoots(roots);
  const nodes: ComponentGraphFlowNode[] = [];
  const edges: Edge[] = [];
  let y = 0;

  for (const group of groups) {
    const groupId = `group:${group.label}`;
    nodes.push({
      id: groupId,
      type: 'componentGroup',
      position: { x: 0, y },
      width: COMPONENT_GROUP_NODE_WIDTH,
      height: COMPONENT_GROUP_NODE_HEIGHT,
      initialWidth: COMPONENT_GROUP_NODE_WIDTH,
      initialHeight: COMPONENT_GROUP_NODE_HEIGHT,
      measured: {
        width: COMPONENT_GROUP_NODE_WIDTH,
        height: COMPONENT_GROUP_NODE_HEIGHT,
      },
      style: {
        width: COMPONENT_GROUP_NODE_WIDTH,
        height: COMPONENT_GROUP_NODE_HEIGHT,
      },
      data: { label: group.label, count: group.roots.length },
      selectable: false,
      draggable: false,
    });

    let groupHeight = 0;
    let rootY = y;
    for (const root of group.roots) {
      const subtreeHeight = layoutComponentTree(
        root,
        0,
        rootY,
        onInspectComponent,
        selectedComponentKey,
        searchMatches,
        nodes,
        edges,
      );
      rootY += subtreeHeight + 34;
      groupHeight += subtreeHeight + 34;
    }

    y += Math.max(groupHeight, 80) + 44;
  }

  return {
    nodes,
    edges,
  };
}

function layoutComponentTree(
  tree: ComponentTreeNode,
  depth: number,
  y: number,
  onInspectComponent: ComponentSelectionHandler,
  selectedComponentKey: string | null,
  searchMatches: Set<string>,
  nodes: ComponentGraphFlowNode[],
  edges: Edge[],
) {
  const graph = getComponentGraph(tree.component);
  const ownHeight = estimateComponentNodeHeight(tree.component);
  const childHeights = tree.children.map(estimateComponentSubtreeHeight);
  const childrenHeight =
    childHeights.reduce((total, height) => total + height, 0) +
    Math.max(0, tree.children.length - 1) * 28;
  const subtreeHeight = Math.max(ownHeight, childrenHeight);
  const nodeY = y + (subtreeHeight - ownHeight) / 2;

  nodes.push({
    id: graph.id,
    type: 'componentNode',
    position: { x: 150 + depth * 430, y: nodeY },
    width: COMPONENT_NODE_WIDTH,
    height: ownHeight,
    initialWidth: COMPONENT_NODE_WIDTH,
    initialHeight: ownHeight,
    measured: {
      width: COMPONENT_NODE_WIDTH,
      height: ownHeight,
    },
      style: {
        width: COMPONENT_NODE_WIDTH,
        height: ownHeight,
      },
    selected: selectedComponentKey === graph.id,
    data: {
      component: tree.component,
      onInspectComponent,
      searchMatch: searchMatches.has(graph.id),
      searchActive: searchMatches.size > 0,
    },
    draggable: false,
  });

  let childY = y + Math.max(0, (subtreeHeight - childrenHeight) / 2);
  for (const [index, child] of tree.children.entries()) {
    const childGraph = getComponentGraph(child.component);
    layoutComponentTree(
      child,
      depth + 1,
      childY,
      onInspectComponent,
      selectedComponentKey,
      searchMatches,
      nodes,
      edges,
    );
    edges.push({
      id: `${graph.id}->${childGraph.id}`,
      source: graph.id,
      sourceHandle: 'out',
      target: childGraph.id,
      targetHandle: 'in',
      type: 'smoothstep',
      className: 'component-flow-edge',
      zIndex: 0,
    });
    childY += childHeights[index] + 28;
  }

  return subtreeHeight;
}

function groupComponentRoots(roots: ComponentTreeNode[]) {
  const groups = new Map<string, ComponentTreeNode[]>();
  for (const root of roots) {
    const graph = getComponentGraph(root.component);
    const label = graph.group ?? 'Components';
    groups.set(label, [...(groups.get(label) ?? []), root]);
  }

  return [...groups.entries()]
    .map(([label, groupRoots]) => ({
      label,
      roots: groupRoots,
      order: Math.min(...groupRoots.map((root) => getComponentGraph(root.component).order)),
    }))
    .sort((left, right) => left.order - right.order || left.label.localeCompare(right.label));
}

function compareComponentNodes(left: ComponentTreeNode, right: ComponentTreeNode) {
  const leftGraph = getComponentGraph(left.component);
  const rightGraph = getComponentGraph(right.component);
  return (
    leftGraph.order - rightGraph.order ||
    (leftGraph.group ?? '').localeCompare(rightGraph.group ?? '') ||
    left.component.type.name.localeCompare(right.component.type.name)
  );
}

function estimateComponentSubtreeHeight(tree: ComponentTreeNode): number {
  const ownHeight = estimateComponentNodeHeight(tree.component);
  if (!tree.children.length) {
    return ownHeight;
  }

  const childrenHeight =
    tree.children.reduce((total, child) => total + estimateComponentSubtreeHeight(child), 0) +
    Math.max(0, tree.children.length - 1) * 28;
  return Math.max(ownHeight, childrenHeight);
}

function estimateComponentNodeHeight(component: ComponentDebugSnapshot) {
  return component.value.error ? 104 : 92;
}

function stabilizeComponentSnapshot(
  previous: ComponentDebugSnapshot,
  next: ComponentDebugSnapshot,
): ComponentDebugSnapshot {
  if (!areDebugTypesEqual(previous.type, next.type) ||
      !areComponentGraphsEqual(getComponentGraph(previous), getComponentGraph(next))) {
    return next;
  }

  if (areComponentValuesEqual(previous.value, next.value)) {
    return previous;
  }

  const structured = stabilizeDebugNode(previous.value.structured, next.value.structured);
  return {
    ...next,
    value: {
      ...next.value,
      structured,
    },
  };
}

function stabilizeDebugNode(
  previous: ComponentValueDebugNode | null,
  next: ComponentValueDebugNode | null,
): ComponentValueDebugNode | null {
  if (!previous || !next) {
    return next;
  }

  if (!areDebugNodeScalarsEqual(previous, next) || previous.children.length !== next.children.length) {
    return next;
  }

  let changed = false;
  const children = next.children.map((child, index) => {
    const stableChild = stabilizeDebugNode(previous.children[index], child);
    if (stableChild !== previous.children[index]) {
      changed = true;
    }

    return stableChild!;
  });
  const elementTemplate = stabilizeDebugNode(previous.elementTemplate, next.elementTemplate);
  if (elementTemplate !== previous.elementTemplate) {
    changed = true;
  }

  if (!changed) {
    return previous;
  }

  return {
    ...next,
    children,
    elementTemplate,
  };
}

function areFlowNodesEqual(left: ComponentGraphFlowNode, right: ComponentGraphFlowNode) {
  if (left.id !== right.id ||
      left.type !== right.type ||
      left.position.x !== right.position.x ||
      left.position.y !== right.position.y ||
      left.width !== right.width ||
      left.height !== right.height ||
      left.initialWidth !== right.initialWidth ||
      left.initialHeight !== right.initialHeight ||
      left.measured?.width !== right.measured?.width ||
      left.measured?.height !== right.measured?.height ||
      left.selectable !== right.selectable ||
      left.draggable !== right.draggable) {
    return false;
  }

  if (left.type === 'componentNode' && right.type === 'componentNode') {
    return (
      left.data.component === right.data.component &&
      left.data.onInspectComponent === right.data.onInspectComponent &&
      left.data.searchMatch === right.data.searchMatch &&
      left.data.searchActive === right.data.searchActive &&
      left.selected === right.selected
    );
  }

  if (left.type === 'componentGroup' && right.type === 'componentGroup') {
    return left.data.label === right.data.label && left.data.count === right.data.count;
  }

  return false;
}

function areFlowEdgesEqual(left: Edge, right: Edge) {
  return (
    left.id === right.id &&
    left.source === right.source &&
    left.sourceHandle === right.sourceHandle &&
    left.target === right.target &&
    left.targetHandle === right.targetHandle &&
    left.type === right.type &&
    left.className === right.className &&
    left.zIndex === right.zIndex
  );
}

function areReferenceArraysEqual<T>(left: readonly T[], right: readonly T[]) {
  return left.length === right.length && left.every((value, index) => value === right[index]);
}

function areComponentGraphsEqual(left: ReturnType<typeof getComponentGraph>, right: ReturnType<typeof getComponentGraph>) {
  return (
    left.id === right.id &&
    left.parentId === right.parentId &&
    left.group === right.group &&
    left.order === right.order &&
    areNullableDebugTypesEqual(left.parentType, right.parentType)
  );
}

function areComponentValuesEqual(left: ComponentValueDebugSnapshot, right: ComponentValueDebugSnapshot) {
  return (
    left.format === right.format &&
    left.payload === right.payload &&
    left.error === right.error &&
    areDebugNodesEqual(left.structured, right.structured)
  );
}

function areDebugNodesEqual(
  left: ComponentValueDebugNode | null,
  right: ComponentValueDebugNode | null,
): boolean {
  if (left === right) {
    return true;
  }

  if (!left || !right) {
    return false;
  }

  return (
    areDebugNodeScalarsEqual(left, right) &&
    left.children.length === right.children.length &&
    left.children.every((child, index) => areDebugNodesEqual(child, right.children[index])) &&
    areDebugNodesEqual(left.elementTemplate, right.elementTemplate)
  );
}

function areDebugNodeScalarsEqual(left: ComponentValueDebugNode, right: ComponentValueDebugNode) {
  return (
    left.kind === right.kind &&
    left.name === right.name &&
    left.editable === right.editable &&
    left.value === right.value &&
    left.error === right.error &&
    areDebugTypesEqual(left.type, right.type) &&
    areNullableDebugTypesEqual(left.elementType, right.elementType) &&
    areStringArraysEqual(left.options, right.options)
  );
}

function areNullableDebugTypesEqual(
  left: ComponentValueDebugNode['type'] | null,
  right: ComponentValueDebugNode['type'] | null,
) {
  if (left === right) {
    return true;
  }

  if (!left || !right) {
    return false;
  }

  return areDebugTypesEqual(left, right);
}

function areDebugTypesEqual(left: ComponentValueDebugNode['type'], right: ComponentValueDebugNode['type']) {
  return (
    left.name === right.name &&
    left.fullName === right.fullName &&
    left.assemblyName === right.assemblyName
  );
}

function areStringArraysEqual(left: string[], right: string[]) {
  return left.length === right.length && left.every((value, index) => value === right[index]);
}

function ComponentNode({ data, selected }: NodeProps<ComponentFlowNode>) {
  const { component, onInspectComponent, searchMatch, searchActive } = data;
  const graph = getComponentGraph(component);
  const initial = component.type.name.trim().charAt(0).toUpperCase() || '?';
  const hasDetails = component.value.payload !== null ||
    component.value.structured !== null ||
    component.value.error !== null;
  const status = component.value.error ? 'Error' : hasDetails ? 'Loaded' : 'Deferred';
  const className = [
    'component-flow-node',
    selected ? 'selected' : '',
    searchMatch ? 'search-match' : '',
    searchActive && !searchMatch ? 'search-dimmed' : '',
  ].filter(Boolean).join(' ');

  return (
    <button
      className={className}
      type="button"
      onClick={() => onInspectComponent(component)}
      title={component.type.fullName}
    >
      <Handle type="target" position={Position.Left} id="in" isConnectable={false} />
      <div className="component-node-corner">
        <span className={component.value.error ? 'component-node-status error' : 'component-node-status'} />
        <span className="component-node-format">{status}</span>
      </div>
      <div className="component-flow-node-head">
        <div className="component-node-icon" aria-hidden>
          {initial}
        </div>
        <div className="component-node-title">
          <strong>{component.type.name}</strong>
          <small>{graph.group ?? component.type.name}</small>
        </div>
      </div>
      <div className="component-node-summary">
        <span>{component.value.format}</span>
        <span>{graph.parentType ? `Child of ${graph.parentType.name}` : 'Root component'}</span>
      </div>
      <Handle type="source" position={Position.Right} id="out" isConnectable={false} />
    </button>
  );
}

export function ComponentInspector({
  entity,
  component,
  onSaveComponent,
  readOnly = false,
}: {
  entity: EntityDebugSnapshot;
  component: ComponentDebugSnapshot;
  onSaveComponent: ComponentMutationExecutor;
  readOnly?: boolean;
}) {
  const [draftPayload, setDraftPayload] = useState(formatComponentValue(component.value));
  const [draftNode, setDraftNode] = useState(() => cloneDebugNode(component.value.structured));
  const graph = getComponentGraph(component);
  const [saving, setSaving] = useState(false);
  const canSave = !readOnly && component.value.format === 'odin-json' && component.value.error === null;

  useEffect(() => {
    setDraftPayload(formatComponentValue(component.value));
    setDraftNode((current) => cloneStableDebugNode(current, component.value.structured));
  }, [component.value]);

  const updateDraftNode = useCallback<StructuredNodeChange>((path, nextNode) => {
    setDraftNode((current) => current ? updateDebugNodeAtPath(current, path, nextNode) : current);
  }, []);

  return (
    <div className="component-inspector-editor">
      <div className="component-inspector-meta">
        <span>{graph.group ?? 'Components'}</span>
        <span>{component.value.format}</span>
      </div>
      {draftNode ? (
        <div className="component-inspector-fields">
          <StructuredValueEditor
            node={draftNode}
            path=""
            onChangeNode={updateDraftNode}
            readOnly={readOnly}
          />
        </div>
      ) : (
        <textarea
          className="component-raw-editor nodrag nopan"
          value={draftPayload}
          onChange={(event) => setDraftPayload(event.target.value)}
          disabled={readOnly}
          spellCheck={false}
        />
      )}
      <div className="action-row component-actions component-inspector-actions">
        <span title={component.type.fullName}>{component.type.name}</span>
        <button
          className="mini-button"
          type="button"
          disabled={!canSave || saving}
          onClick={async () => {
            setSaving(true);
            try {
              await onSaveComponent(entity, component, {
                ...component.value,
                payload: draftNode ? component.value.payload : draftPayload,
                structured: draftNode,
                error: null,
              });
            } finally {
              setSaving(false);
            }
          }}
        >
          {saving ? 'Saving' : 'Save'}
        </button>
      </div>
    </div>
  );
}

function ComponentGroupNode({ data }: NodeProps<ComponentGroupFlowNode>) {
  return (
    <div className="component-flow-group">
      <strong>{data.label}</strong>
      <span>{data.count}</span>
    </div>
  );
}

const StructuredValueEditor = memo(function StructuredValueEditor({
  node,
  path,
  onChangeNode,
  readOnly,
  depth = 0,
}: {
  node: ComponentValueDebugNode;
  path: string;
  onChangeNode: StructuredNodeChange;
  readOnly: boolean;
  depth?: number;
}) {
  if (node.kind === 'object') {
    return (
      <div className={depth === 0 ? 'structured-editor' : 'field-group'}>
        {depth > 0 ? <FieldHeader node={node} /> : null}
        {node.children.length ? (
          node.children.map((child, index) => (
            <StructuredValueEditor
              key={`${child.name ?? index}:${child.type.fullName}`}
              node={child}
              path={appendDebugNodePath(path, index)}
              depth={depth + 1}
              onChangeNode={onChangeNode}
              readOnly={readOnly}
            />
          ))
        ) : (
          <span className="field-note">No public fields</span>
        )}
      </div>
    );
  }

  if (node.kind === 'list') {
    return (
      <ListField
        node={node}
        path={path}
        onChangeNode={onChangeNode}
        readOnly={readOnly}
        depth={depth}
      />
    );
  }

  return <ScalarField node={node} path={path} onChangeNode={onChangeNode} readOnly={readOnly} />;
});

const ListField = memo(function ListField({
  node,
  path,
  onChangeNode,
  readOnly,
  depth,
}: {
  node: ComponentValueDebugNode;
  path: string;
  onChangeNode: StructuredNodeChange;
  readOnly: boolean;
  depth: number;
}) {
  const canAdd = !readOnly && node.editable && node.elementTemplate !== null;

  return (
    <div className="field-group list-field">
      <div className="field-header">
        <FieldHeader node={node} />
        <button
          className="mini-button"
          type="button"
          disabled={!canAdd}
          onClick={() => onChangeNode(path, addListItem(node))}
        >
          Add
        </button>
      </div>
      {node.children.length ? (
        node.children.map((child, index) => (
          <div key={`${child.name ?? index}:${child.type.fullName}`} className="list-item">
            <StructuredValueEditor
              node={child}
              path={appendDebugNodePath(path, index)}
              depth={depth + 1}
              onChangeNode={onChangeNode}
              readOnly={readOnly}
            />
            <button
              className="mini-button danger"
              type="button"
              disabled={readOnly || !node.editable}
              onClick={() => onChangeNode(path, removeListItem(node, index))}
            >
              Remove
            </button>
          </div>
        ))
      ) : (
        <span className="field-note">Empty list</span>
      )}
    </div>
  );
});

const ScalarField = memo(function ScalarField({
  node,
  path,
  onChangeNode,
  readOnly,
}: {
  node: ComponentValueDebugNode;
  path: string;
  onChangeNode: StructuredNodeChange;
  readOnly: boolean;
}) {
  const disabled = readOnly || !node.editable;

  return (
    <label className={disabled ? 'field-row readonly' : 'field-row'}>
      <FieldHeader node={node} />
      {renderScalarControl(node, (nextNode) => onChangeNode(path, nextNode), disabled)}
    </label>
  );
});

function FieldHeader({ node }: { node: ComponentValueDebugNode }) {
  return (
    <span className="field-label">
      <strong>{formatFieldName(node)}</strong>
      <small>{formatTypeName(node.type.name)}</small>
      {node.error ? <em>{node.error}</em> : null}
    </span>
  );
}

function renderScalarControl(
  node: ComponentValueDebugNode,
  onChange: (next: ComponentValueDebugNode) => void,
  disabled: boolean,
) {
  if (node.kind === 'boolean') {
    return (
      <input
        type="checkbox"
        checked={node.value === 'true'}
        disabled={disabled}
        onChange={(event) => onChange({ ...node, value: event.target.checked ? 'true' : 'false' })}
      />
    );
  }

  if (node.kind === 'integer' || node.kind === 'number') {
    return (
      <input
        type="number"
        step={node.kind === 'integer' ? 1 : 'any'}
        value={node.value ?? ''}
        disabled={disabled}
        onChange={(event) => onChange({ ...node, value: event.target.value })}
      />
    );
  }

  if (node.kind === 'enum') {
    return (
      <select
        value={node.value ?? ''}
        disabled={disabled}
        onChange={(event) => onChange({ ...node, value: event.target.value })}
      >
        {node.options.map((option) => (
          <option key={option} value={option}>
            {option}
          </option>
        ))}
      </select>
    );
  }

  if (node.kind === 'string' || node.kind === 'null') {
    return (
      <input
        type="text"
        value={node.value ?? ''}
        disabled={disabled || node.kind === 'null'}
        onChange={(event) => onChange({ ...node, value: event.target.value })}
      />
    );
  }

  return <span className="field-note">{node.error ?? node.kind}</span>;
}

function cloneDebugNode(node: ComponentValueDebugNode | null) {
  return node ? (JSON.parse(JSON.stringify(node)) as ComponentValueDebugNode) : null;
}

function cloneStableDebugNode(
  previous: ComponentValueDebugNode | null,
  next: ComponentValueDebugNode | null,
): ComponentValueDebugNode | null {
  if (!next) {
    return null;
  }

  if (!previous ||
      !areDebugNodeScalarsEqual(previous, next) ||
      previous.children.length !== next.children.length) {
    return cloneDebugNode(next);
  }

  let changed = false;
  const children = next.children.map((child, index) => {
    const stableChild = cloneStableDebugNode(previous.children[index], child);
    if (stableChild !== previous.children[index]) {
      changed = true;
    }

    return stableChild!;
  });
  const elementTemplate = cloneStableDebugNode(previous.elementTemplate, next.elementTemplate);
  if (elementTemplate !== previous.elementTemplate) {
    changed = true;
  }

  if (!changed) {
    return previous;
  }

  return {
    ...next,
    children,
    elementTemplate,
  };
}

function appendDebugNodePath(path: string, index: number) {
  return path ? `${path}.${index}` : String(index);
}

function updateDebugNodeAtPath(
  node: ComponentValueDebugNode,
  path: string,
  nextNode: ComponentValueDebugNode,
): ComponentValueDebugNode {
  if (!path) {
    return nextNode;
  }

  const [head, ...tail] = path.split('.');
  const childIndex = Number(head);
  if (!Number.isInteger(childIndex) || childIndex < 0 || childIndex >= node.children.length) {
    return node;
  }

  return {
    ...node,
    children: node.children.map((child, index) =>
      index === childIndex
        ? updateDebugNodeAtPath(child, tail.join('.'), nextNode)
        : child,
    ),
  };
}

function addListItem(node: ComponentValueDebugNode) {
  if (!node.elementTemplate) {
    return node;
  }

  return {
    ...node,
    children: renameListChildren([
      ...node.children,
      {
        ...cloneDebugNode(node.elementTemplate)!,
        editable: true,
      },
    ]),
  };
}

function removeListItem(node: ComponentValueDebugNode, index: number) {
  return {
    ...node,
    children: renameListChildren(node.children.filter((_, candidateIndex) => candidateIndex !== index)),
  };
}

function renameListChildren(children: ComponentValueDebugNode[]) {
  return children.map((child, index) => ({
    ...child,
    name: `[${index}]`,
  }));
}

function formatFieldName(node: ComponentValueDebugNode) {
  return node.name ?? node.type.name;
}

function formatTypeName(typeName: string) {
  const tickIndex = typeName.indexOf('`');
  return tickIndex >= 0 ? typeName.slice(0, tickIndex) : typeName;
}

function findMatchingComponentIds(components: ComponentDebugSnapshot[], query: string) {
  const matches = new Set<string>();
  if (!query.trim()) {
    return matches;
  }

  for (const component of components) {
    const graph = getComponentGraph(component);
    if (matchesQuery(
      [
        component.type.name,
        component.type.fullName,
        component.value.format,
        component.value.payload ?? '',
        graph.group ?? '',
        graph.parentType?.name ?? '',
        graph.parentType?.fullName ?? '',
      ],
      query,
    )) {
      matches.add(graph.id);
    }
  }

  return matches;
}

function matchesQuery(parts: unknown[], query: string) {
  const normalized = query.trim().toLowerCase();
  if (!normalized) {
    return true;
  }

  return parts
    .filter((part) => part !== null && part !== undefined)
    .join(' ')
    .toLowerCase()
    .includes(normalized);
}

function formatComponentValue(value: ComponentValueDebugSnapshot) {
  if (value.error) {
    return `${value.format} error: ${value.error}`;
  }

  if (!value.payload) {
    return `${value.format}: <empty>`;
  }

  try {
    return JSON.stringify(JSON.parse(value.payload), null, 2);
  } catch {
    return value.payload;
  }
}
