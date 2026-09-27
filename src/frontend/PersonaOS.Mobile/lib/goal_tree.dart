/// The goal tree as the Goals and Timeline tabs draw it: parents before their children, each
/// goal's ancestors (so folding one hides what is under it), and swim lanes (a top-level goal
/// and everything under it). The web has the same helpers in goals.service.ts.
library;

import 'api/personaos_api.dart';

/// A goal in the tree.
class TreeRow {
  TreeRow(this.goal, this.depth, this.ancestors, this.lane, this.hasChildren);

  final Goal goal;
  final int depth;

  /// Its parent, its parent's parent, …: folding any of them hides it.
  final List<int> ancestors;

  /// A top-level goal and everything under it share a lane.
  final int lane;

  /// Whether any goal in the list sits under it, i.e. whether it folds.
  final bool hasChildren;
}

/// Parents before their children, each child right after its parent, with its depth.
List<(Goal, int)> inTreeOrder(List<Goal> goals) {
  final ids = goals.map((g) => g.id).toSet();
  final children = <int, List<Goal>>{};
  for (final goal in goals) {
    if (goal.parentId != null && ids.contains(goal.parentId)) {
      children.putIfAbsent(goal.parentId!, () => []).add(goal);
    }
  }
  final out = <(Goal, int)>[];
  void add(Goal goal, int depth) {
    out.add((goal, depth));
    for (final child in children[goal.id] ?? const <Goal>[]) {
      add(child, depth + 1);
    }
  }

  for (final goal in goals) {
    if (goal.parentId == null || !ids.contains(goal.parentId)) add(goal, 0);
  }
  return out;
}

/// Every goal in tree order, with what folding and lanes need.
List<TreeRow> treeRows(List<Goal> goals) {
  final byId = {for (final g in goals) g.id: g};
  final parents = goals.map((g) => g.parentId).whereType<int>().where(byId.containsKey).toSet();
  var lane = -1;
  return [
    for (final (goal, depth) in inTreeOrder(goals))
      () {
        final ancestors = [for (var p = goal.parentId; p != null && byId.containsKey(p); p = byId[p]!.parentId) p];
        if (ancestors.isEmpty) lane++;
        return TreeRow(goal, depth, ancestors, lane, parents.contains(goal.id));
      }(),
  ];
}

/// The rows still shown when the goals in [collapsed] are folded shut.
List<TreeRow> unfolded(List<TreeRow> rows, Set<int> collapsed) =>
    rows.where((r) => !r.ancestors.any(collapsed.contains)).toList();

/// Consecutive rows grouped by lane.
List<List<TreeRow>> byLane(List<TreeRow> rows) {
  final lanes = <List<TreeRow>>[];
  for (final row in rows) {
    if (lanes.isEmpty || lanes.last.first.lane != row.lane) lanes.add([]);
    lanes.last.add(row);
  }
  return lanes;
}
