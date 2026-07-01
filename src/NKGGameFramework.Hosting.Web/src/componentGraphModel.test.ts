import assert from 'node:assert/strict';
import test from 'node:test';
import { countComponentGroups, getComponentGraph } from './componentGraphModel.ts';
import type { ComponentDebugSnapshot } from './types.ts';

test('getComponentGraph derives stable fallback metadata', () => {
  const component = createComponent('Health', 'Game.Health', 'Game.Assembly');

  assert.deepEqual(getComponentGraph(component), {
    id: 'Game.Assembly:Game.Health',
    parentId: null,
    parentType: null,
    group: null,
    order: 0,
  });
});

test('getComponentGraph preserves explicit graph metadata', () => {
  const parentType = { name: 'Stats', fullName: 'Game.Stats', assemblyName: 'Game.Assembly' };
  const component = createComponent('Health', 'Game.Health', 'Game.Assembly', {
    id: 'health',
    parentId: 'stats',
    parentType,
    group: 'Stats',
    order: 20,
  });

  assert.deepEqual(getComponentGraph(component), {
    id: 'health',
    parentId: 'stats',
    parentType,
    group: 'Stats',
    order: 20,
  });
});

test('countComponentGroups combines explicit and fallback groups', () => {
  const components = [
    createComponent('Health', 'Game.Health', 'Game.Assembly', { group: 'Stats' }),
    createComponent('Mana', 'Game.Mana', 'Game.Assembly', { group: 'Stats' }),
    createComponent('Position', 'Game.Position', 'Game.Assembly'),
  ];

  assert.equal(countComponentGroups(components), 2);
});

function createComponent(
  name: string,
  fullName: string,
  assemblyName: string,
  graph?: Partial<ComponentDebugSnapshot['graph']>,
): ComponentDebugSnapshot {
  return {
    type: { name, fullName, assemblyName },
    value: {
      format: 'none',
      payload: null,
      error: null,
      structured: null,
    },
    graph: graph as ComponentDebugSnapshot['graph'],
  };
}
