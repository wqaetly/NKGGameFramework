import { useEffect, useMemo, useState } from 'react';
import {
  Background,
  Controls,
  Handle,
  MarkerType,
  Position,
  ReactFlow,
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
  entity: EntityDebugSnapshot;
  component: ComponentDebugSnapshot;
  onSaveComponent: ComponentMutationExecutor;
};

type ComponentGroupNodeData = {
  label: string;
  count: number;
};

type ComponentFlowNode = Node<ComponentNodeData, 'componentNode'>;
type ComponentGroupFlowNode = Node<ComponentGroupNodeData, 'componentGroup'>;
type ComponentGraphFlowNode = ComponentFlowNode | ComponentGroupFlowNode;

type ComponentTreeNode = {
  component: ComponentDebugSnapshot;
  children: ComponentTreeNode[];
};

const componentNodeTypes = {
  componentNode: ComponentNode,
  componentGroup: ComponentGroupNode,
} satisfies NodeTypes;

export function ComponentGraphCanvas({
  entity,
  query,
  onSaveComponent,
}: {
  entity: EntityDebugSnapshot;
  query: string;
  onSaveComponent: ComponentMutationExecutor;
}) {
  const graph = useMemo(
    () => buildComponentGraph(
      {
        ...entity,
        components: filterComponents(entity.components, query),
      },
      onSaveComponent,
    ),
    [entity, query, onSaveComponent],
  );

  return (
    <div className="component-flow">
      {graph.nodes.length ? (
        <ReactFlow
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

function buildComponentGraph(
  entity: EntityDebugSnapshot,
  onSaveComponent: ComponentMutationExecutor,
) {
  const componentById = new Map<string, ComponentTreeNode>();

  for (const component of entity.components) {
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
        entity,
        onSaveComponent,
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
  entity: EntityDebugSnapshot,
  onSaveComponent: ComponentMutationExecutor,
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
    data: { entity, component: tree.component, onSaveComponent },
    draggable: false,
  });

  let childY = y + Math.max(0, (subtreeHeight - childrenHeight) / 2);
  for (const [index, child] of tree.children.entries()) {
    const childGraph = getComponentGraph(child.component);
    layoutComponentTree(
      child,
      depth + 1,
      childY,
      entity,
      onSaveComponent,
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
  const structuredRows = component.value.structured ? countStructuredRows(component.value.structured) : 5;
  return 74 + Math.min(280, Math.max(64, structuredRows * 27));
}

function countStructuredRows(node: ComponentValueDebugNode): number {
  if (node.kind === 'object') {
    return Math.max(1, node.children.reduce((total, child) => total + countStructuredRows(child), 0));
  }

  if (node.kind === 'list') {
    return Math.max(2, node.children.reduce((total, child) => total + countStructuredRows(child), 1));
  }

  return 1;
}

function ComponentNode({ data, selected }: NodeProps<ComponentFlowNode>) {
  const { entity, component, onSaveComponent } = data;
  const [draftPayload, setDraftPayload] = useState(formatComponentValue(component.value));
  const [draftNode, setDraftNode] = useState(() => cloneDebugNode(component.value.structured));
  const [saving, setSaving] = useState(false);
  const graph = getComponentGraph(component);
  const initial = component.type.name.trim().charAt(0).toUpperCase() || '?';

  useEffect(() => {
    setDraftPayload(formatComponentValue(component.value));
    setDraftNode(cloneDebugNode(component.value.structured));
  }, [component.value]);

  return (
    <div className={selected ? 'component-flow-node selected' : 'component-flow-node'}>
      <Handle type="target" position={Position.Left} id="in" isConnectable={false} />
      <div className="component-node-corner">
        <span className={component.value.error ? 'component-node-status error' : 'component-node-status'} />
        <span className="component-node-format">{component.value.format}</span>
      </div>
      <div className="component-flow-node-head">
        <div className="component-node-icon" aria-hidden>
          {initial}
        </div>
        <div className="component-node-title">
          <strong>{component.type.name}</strong>
          <small>{graph.group ?? component.type.fullName}</small>
        </div>
      </div>
      {draftNode ? (
        <div className="component-flow-node-fields nodrag nopan">
          <StructuredValueEditor node={draftNode} onChange={setDraftNode} />
        </div>
      ) : (
        <textarea
          className="component-raw-editor nodrag nopan"
          value={draftPayload}
          onChange={(event) => setDraftPayload(event.target.value)}
          spellCheck={false}
        />
      )}
      <div className="action-row component-actions component-node-actions nodrag nopan">
        <span>{component.value.format}</span>
        <button
          className="mini-button"
          type="button"
          disabled={saving || component.value.error !== null}
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
      <Handle type="source" position={Position.Right} id="out" isConnectable={false} />
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

function StructuredValueEditor({
  node,
  onChange,
  depth = 0,
}: {
  node: ComponentValueDebugNode;
  onChange: (next: ComponentValueDebugNode) => void;
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
              depth={depth + 1}
              onChange={(nextChild) => onChange(updateChild(node, index, nextChild))}
            />
          ))
        ) : (
          <span className="field-note">No public fields</span>
        )}
      </div>
    );
  }

  if (node.kind === 'list') {
    return <ListField node={node} onChange={onChange} depth={depth} />;
  }

  return <ScalarField node={node} onChange={onChange} />;
}

function ListField({
  node,
  onChange,
  depth,
}: {
  node: ComponentValueDebugNode;
  onChange: (next: ComponentValueDebugNode) => void;
  depth: number;
}) {
  const canAdd = node.editable && node.elementTemplate !== null;

  return (
    <div className="field-group list-field">
      <div className="field-header">
        <FieldHeader node={node} />
        <button
          className="mini-button"
          type="button"
          disabled={!canAdd}
          onClick={() => onChange(addListItem(node))}
        >
          Add
        </button>
      </div>
      {node.children.length ? (
        node.children.map((child, index) => (
          <div key={`${child.name ?? index}:${child.type.fullName}`} className="list-item">
            <StructuredValueEditor
              node={child}
              depth={depth + 1}
              onChange={(nextChild) => onChange(updateChild(node, index, nextChild))}
            />
            <button
              className="mini-button danger"
              type="button"
              disabled={!node.editable}
              onClick={() => onChange(removeListItem(node, index))}
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
}

function ScalarField({
  node,
  onChange,
}: {
  node: ComponentValueDebugNode;
  onChange: (next: ComponentValueDebugNode) => void;
}) {
  const disabled = !node.editable;

  return (
    <label className={disabled ? 'field-row readonly' : 'field-row'}>
      <FieldHeader node={node} />
      {renderScalarControl(node, onChange, disabled)}
    </label>
  );
}

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

function updateChild(node: ComponentValueDebugNode, index: number, child: ComponentValueDebugNode) {
  return {
    ...node,
    children: node.children.map((candidate, candidateIndex) =>
      candidateIndex === index ? child : candidate,
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

function filterComponents(components: ComponentDebugSnapshot[], query: string) {
  return components.filter((component) => {
    const graph = getComponentGraph(component);
    return matchesQuery(
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
    );
  });
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
