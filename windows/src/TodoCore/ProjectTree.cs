namespace TodoCore;

/// <summary>One line of the project tree, in display order.</summary>
public sealed record ProjectOutlineRow(Project Project, int Depth, bool HasChildren)
{
    public Guid Id => Project.Id;
}

/// <summary>Projects nest through <see cref="Project.ParentId"/>; these read the flat list as a tree.</summary>
public static class ProjectTree
{
    /// <summary>
    /// Every project once, parents directly above their children. A project whose parent is missing, or that sits
    /// in a loop (a hand-edited file), still appears, at the top level.
    /// </summary>
    public static IReadOnlyList<ProjectOutlineRow> Outline(this IReadOnlyList<Project> projects)
    {
        ArgumentNullException.ThrowIfNull(projects);
        var ids = projects.Select(p => p.Id).ToHashSet();
        var children = new Dictionary<Guid, List<Project>>();
        foreach (var project in projects)
        {
            if (project.ParentId is not { } parent || !ids.Contains(parent)) continue;
            if (!children.TryGetValue(parent, out var list)) children[parent] = list = new List<Project>();
            list.Add(project);
        }

        var rows = new List<ProjectOutlineRow>();
        var visited = new HashSet<Guid>();

        void Visit(Project project, int depth)
        {
            if (!visited.Add(project.Id)) return;
            var kids = children.GetValueOrDefault(project.Id) ?? new List<Project>();
            rows.Add(new ProjectOutlineRow(project, depth, kids.Count > 0));
            foreach (var kid in kids) Visit(kid, depth + 1);
        }

        foreach (var project in projects)
        {
            if (project.ParentId is not { } parent || !ids.Contains(parent)) Visit(project, 0);
        }

        foreach (var project in projects) Visit(project, 0);
        return rows;
    }

    /// <summary>The outline without whatever sits under a collapsed project.</summary>
    public static IReadOnlyList<ProjectOutlineRow> Outline(this IReadOnlyList<Project> projects, IReadOnlySet<Guid> collapsed)
    {
        ArgumentNullException.ThrowIfNull(collapsed);
        var rows = new List<ProjectOutlineRow>();
        int? hiddenBelow = null;
        foreach (var row in projects.Outline())
        {
            if (hiddenBelow is { } depth && row.Depth > depth) continue;
            hiddenBelow = null;
            rows.Add(row);
            if (row.HasChildren && collapsed.Contains(row.Id)) hiddenBelow = row.Depth;
        }

        return rows;
    }

    /// <summary>The project and everything under it.</summary>
    public static IReadOnlySet<Guid> SubtreeIds(this IReadOnlyList<Project> projects, Guid id)
    {
        ArgumentNullException.ThrowIfNull(projects);
        var result = new HashSet<Guid> { id };
        var frontier = new Stack<Guid>();
        frontier.Push(id);
        while (frontier.TryPop(out var current))
        {
            foreach (var project in projects)
            {
                if (project.ParentId == current && result.Add(project.Id)) frontier.Push(project.Id);
            }
        }

        return result;
    }

    /// <summary>Where a project may be placed: anywhere except inside itself.</summary>
    public static IReadOnlyList<ProjectOutlineRow> ParentChoices(this IReadOnlyList<Project> projects, Guid? id)
    {
        if (id is not { } value) return projects.Outline();
        var excluded = projects.SubtreeIds(value);
        return projects.Outline().Where(row => !excluded.Contains(row.Id)).ToList();
    }

    /// <summary>Puts a project directly above <paramref name="beforeId"/>, as its sibling. False when that makes no sense.</summary>
    public static bool MoveProject(this List<Project> projects, Guid id, Guid beforeId)
    {
        ArgumentNullException.ThrowIfNull(projects);
        var from = projects.FindIndex(p => p.Id == id);
        var target = projects.Find(p => p.Id == beforeId);
        if (id == beforeId || from < 0 || target is null || projects.SubtreeIds(id).Contains(beforeId)) return false;
        var project = projects[from] with { ParentId = target.ParentId };
        projects.RemoveAt(from);
        var to = projects.FindIndex(p => p.Id == beforeId);
        projects.Insert(to < 0 ? projects.Count : to, project);
        return true;
    }
}
