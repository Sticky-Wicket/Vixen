﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Vixen.Sys;
using Vixen.Module.Property;
using System.Reflection;
using CommonElements;

namespace VixenApplication
{
	public partial class ConfigChannels : Form
	{
		private ChannelNode _displayedNode;

		// sets of data to keep track of which items in the treeview are open, selected, visible etc.
		// so that when we reload the tree, we can keep it looking relatively consistent with what the
		// user had before.
		private HashSet<string> _expandedNodes;		// TreeNode paths that are expanded
		private HashSet<string> _selectedNodes;		// TreeNode paths that are selected
		private List<string> _topDisplayedNodes;	// TreeNode paths that are at the top of the view. Should only
													// need one, but will have multiple in case the top node is deleted.
		private ToolTip _tooltip;

		public ConfigChannels()
		{
			InitializeComponent();
			_displayedNode = null;
			_tooltip = new ToolTip();

			multiSelectTreeviewChannelsGroups.DragFinishing += multiSelectTreeviewChannelsGroupsDragFinishingHandler;
			multiSelectTreeviewChannelsGroups.DragOverVerify += multiSelectTreeviewChannelsGroupsDragVerifyHandler;

			SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
		}

		private void ConfigChannels_Load(object sender, EventArgs e)
		{
			PopulateNodeTree();
			PopulateFormWithNode(null, true);
			PopulateComboBoxControllers();
		}


		#region Node Tree population & display
		private void PopulateNodeTree()
		{
			// save metadata that is currently in the treeview
			_expandedNodes = new HashSet<string>();
			_selectedNodes = new HashSet<string>();
			_topDisplayedNodes = new List<string>();

			SaveTreeNodeState(multiSelectTreeviewChannelsGroups.Nodes);
			SaveTreeNodeTopVisible();

			// clear the treeview, and repopulate it
			multiSelectTreeviewChannelsGroups.BeginUpdate();
			multiSelectTreeviewChannelsGroups.Nodes.Clear();

			foreach(ChannelNode channel in VixenSystem.Nodes.RootNodes) {
				AddNodeToTree(multiSelectTreeviewChannelsGroups.Nodes, channel);
			}

			// go through all the data we saved, and try to update the treeview to look
			// like it used to (expanded nodes, selected nodes, node at the top)

			foreach (string node in _expandedNodes) {
				TreeNode resultNode = FindNodeInTreeAtPath(multiSelectTreeviewChannelsGroups, node);

				if (resultNode != null) {
					resultNode.Expand();
				}
			}

			foreach (string node in _selectedNodes) {
				TreeNode resultNode = FindNodeInTreeAtPath(multiSelectTreeviewChannelsGroups, node);

				if (resultNode != null) {
					multiSelectTreeviewChannelsGroups.AddSelectedNode(resultNode);
				}
			}

			multiSelectTreeviewChannelsGroups.EndUpdate();

			// see stackoverflow.com/questions/626315/winforms-listview-remembering-scrolled-location-on-reload .
			// we can only set the topNode after EndUpdate(). Also, it might throw an exception -- weird?
			foreach (string node in _topDisplayedNodes) {
				TreeNode resultNode = FindNodeInTreeAtPath(multiSelectTreeviewChannelsGroups, node);

				if (resultNode != null) {
					try {
						multiSelectTreeviewChannelsGroups.TopNode = resultNode;
					} catch (Exception) {
						VixenSystem.Logging.Warn("ConfigChannels: exception caught trying to set TopNode.");
					}
					break;
				}
			}
		}

		private string GenerateTreeNodeFullPath(TreeNode node, string separator)
		{
			string result = node.Name;
			TreeNode parent = node.Parent;
			while (parent != null) {
				result = parent.Name + separator + result;
				parent = parent.Parent;
			}

			return result;
		}

		private TreeNode FindNodeInTreeAtPath(TreeView tree, string path)
		{
			string[] subnodes = path.Split(new string[] { tree.PathSeparator }, StringSplitOptions.None);
			TreeNodeCollection searchNodes = tree.Nodes;
			TreeNode currentNode = null;
			foreach (string search in subnodes) {
				bool found = false;
				foreach (TreeNode tn in searchNodes) {
					if (tn.Name == search) {
						found = true;
						currentNode = tn;
						searchNodes = tn.Nodes;
						break;
					}
				}
				if (!found) {
					currentNode = null;
					break;
				}
			}

			return currentNode;
		}

		private void SaveTreeNodeState(TreeNodeCollection collection)
		{
			foreach (TreeNode tn in collection) {
				if (tn.IsExpanded) {
					_expandedNodes.Add(GenerateTreeNodeFullPath(tn, multiSelectTreeviewChannelsGroups.PathSeparator));
				}

				if (multiSelectTreeviewChannelsGroups.SelectedNodes.Contains(tn)) {
					_selectedNodes.Add(GenerateTreeNodeFullPath(tn, multiSelectTreeviewChannelsGroups.PathSeparator));
				}

				SaveTreeNodeState(tn.Nodes);
			}
		}

		private void SaveTreeNodeTopVisible()
		{
			// this will iterate through all root nodes -- starting with the topmost visible
			// node -- adding their path to a list in order. Later on, when refreshing the tree,
			// we can try them in order to place at the top of the display. We should only
			// need a single node, but in case the top node gets deleted (or the top few),
			// we keep a list of 'preferred' nodes.
			if (multiSelectTreeviewChannelsGroups.Nodes.Count > 0) {
				TreeNode current = multiSelectTreeviewChannelsGroups.TopNode;
				while (current != null) {
					_topDisplayedNodes.Add(GenerateTreeNodeFullPath(current, multiSelectTreeviewChannelsGroups.PathSeparator));
					current = current.NextNode;
				}
			}
		}

		private void AddNodeToTree(TreeNodeCollection collection, ChannelNode channel)
		{
			TreeNode addedNode = new TreeNode();
			addedNode.Name = channel.Id.ToString();
			addedNode.Text = channel.Name;
			addedNode.Tag = channel;

			if (channel.Children.Count() <= 0) {
				if (channel.Channel != null && channel.Channel.Patch.Count() > 0) {
					if (channel.Channel.Masked)
						addedNode.ImageKey = addedNode.SelectedImageKey = "RedBall";
					else
						addedNode.ImageKey = addedNode.SelectedImageKey = "GreenBall";
				} else
					addedNode.ImageKey = addedNode.SelectedImageKey = "WhiteBall";
			} else {
				addedNode.ImageKey = addedNode.SelectedImageKey = "Group";
			}

			collection.Add(addedNode);

			foreach (ChannelNode childNode in channel.Children) {
				AddNodeToTree(addedNode.Nodes, childNode);
			}
		}
		#endregion


		#region Form controls population & display

		private void PopulateFormWithNode(ChannelNode node, bool forceUpdate)
		{
			if (node == _displayedNode && !forceUpdate)
				return;

			_displayedNode = node;

			PopulateGeneralNodeInfo(node);
			PopulateCurrentPatchesArea(node);
			PopulateAddPatchArea(node);
			PopulatePropertiesArea(node);

			groupBoxSelectedNode.Enabled = (node != null);
		}

		private void PopulateGeneralNodeInfo(ChannelNode node)
		{
			if (node == null) {
				labelParents.Text = "";
				_tooltip.SetToolTip(labelParents, null);
				textBoxName.Text = "";
			} else {
				// update the label with parent info about the node. Any good suggestions or improvements for this?
				int parentCount = GetNodeParentGroupCount(node);
				List<string> parents = GetNodeParentGroupNames(node);
				string labelString = "", tooltipString = "";
				labelString = "This node is in " + parentCount + " group" + ((parentCount != 1) ? "s" : "") + ((parentCount == 0) ? "." : ": ");
				tooltipString = labelString + "\r\n\r\n";
				foreach (string p in parents) {
					labelString = labelString + p + ", ";
					tooltipString = tooltipString + p + "\r\n";
				}
				labelParents.Text = labelString.TrimEnd(new char[] { ' ', ',' });
				tooltipString = tooltipString.TrimEnd(new char[] { '\r', '\n' });
				if (labelString.Length > 100) {
					_tooltip.SetToolTip(labelParents, tooltipString);
				} else {
					_tooltip.SetToolTip(labelParents, null);
				}

				textBoxName.Text = node.Name;
			}
		}

		private void PopulatePropertiesArea(ChannelNode node)
		{
			listViewProperties.BeginUpdate();
			listViewProperties.Items.Clear();
			if (node != null) {
				foreach (IPropertyModuleInstance property in node.Properties) {
					ListViewItem item = new ListViewItem();
					item.Text = property.Descriptor.TypeName;
					item.Tag = property;
					listViewProperties.Items.Add(item);
				}

				listViewProperties.SelectedItems.Clear();
			}
			listViewProperties.EndUpdate();

			groupBoxProperties.Enabled = (node != null);
		}

		private void PopulateCurrentPatchesArea(ChannelNode node)
		{
			listViewPatches.BeginUpdate();
			listViewPatches.Items.Clear();

			if (node != null && node.Channel != null) {
				foreach (ControllerReference patch in node.Channel.Patch.ControllerReferences) {
					ListViewItem item = new ListViewItem();
					item.Text = patch.ToString();
					item.Tag = patch;
					listViewPatches.Items.Add(item);
				}
				buttonRemovePatch.Enabled = (listViewPatches.SelectedItems.Count > 0);

				if (checkBoxDisableOutputs.Checked != node.Channel.Masked)
					checkBoxDisableOutputs.Checked = node.Channel.Masked;
			} else {
				checkBoxDisableOutputs.Checked = false;
			}

			listViewPatches.EndUpdate();

			groupBoxPatches.Enabled = (node != null && node.Children.Count() == 0);
		}

		private void PopulateAddPatchArea(ChannelNode node)
		{
			numericUpDownPatchOutputSelect.Enabled = (comboBoxPatchControllerSelect.SelectedIndex >= 0);
			buttonAddPatch.Enabled = (comboBoxPatchControllerSelect.SelectedIndex >= 0);

			if (comboBoxPatchControllerSelect.SelectedIndex >= 0) {
				OutputController oc = VixenSystem.Controllers.Get((Guid)comboBoxPatchControllerSelect.SelectedValue);
				numericUpDownPatchOutputSelect.Maximum = oc.OutputCount;
				if (oc.OutputCount == 0)
					numericUpDownPatchOutputSelect.Minimum = oc.OutputCount;
				else
					numericUpDownPatchOutputSelect.Minimum = 1;
			} else {
				numericUpDownPatchOutputSelect.Maximum = 0;
				numericUpDownPatchOutputSelect.Minimum = 0;
				numericUpDownPatchOutputSelect.Value = 0;
			}

			groupBoxAddPatch.Enabled = (node != null && node.Children.Count() == 0);
		}


		private void PopulateComboBoxControllers()
		{
			comboBoxPatchControllerSelect.BeginUpdate();
			comboBoxPatchControllerSelect.Items.Clear();

			List<ComboBoxControllerItem> controllerEntries = new List<ComboBoxControllerItem>();
			foreach (OutputController oc in VixenSystem.Controllers) {
				ComboBoxControllerItem item = new ComboBoxControllerItem { Name = oc.Name, Id = oc.Id };
				controllerEntries.Add(item);
			}

			comboBoxPatchControllerSelect.DisplayMember = "Name";
			comboBoxPatchControllerSelect.ValueMember = "Id";
			comboBoxPatchControllerSelect.DataSource = controllerEntries;
			comboBoxPatchControllerSelect.SelectedIndex = -1;

			comboBoxPatchControllerSelect.EndUpdate();
		}

		#endregion


		#region Form buttons

		private void buttonAddNode_Click(object sender, EventArgs e)
		{
			using (CommonElements.TextDialog textDialog = new CommonElements.TextDialog("Node Name?")) {
				if (textDialog.ShowDialog() == DialogResult.OK) {
					if (textDialog.Response == "")
						VixenSystem.Nodes.AddNewNode("New Node");
					else
						VixenSystem.Nodes.AddNewNode(textDialog.Response);

					PopulateNodeTree();
				}
			}
		}

		private void buttonDeleteNode_Click(object sender, EventArgs e)
		{
			if (multiSelectTreeviewChannelsGroups.SelectedNodes.Count > 0)
			{
				string message, title;
				if (multiSelectTreeviewChannelsGroups.SelectedNodes.Count > 1) {
					message = "Are you sure you want to delete the selected nodes?";
					title = "Delete Nodes?";
				} else {
					message = "Are you sure you want to delete the selected node?";
					title = "Delete Node?";
				}
				if (MessageBox.Show(message, title, MessageBoxButtons.OKCancel) == DialogResult.OK) {
					foreach (TreeNode tn in multiSelectTreeviewChannelsGroups.SelectedNodes) {
						ChannelNode cn = tn.Tag as ChannelNode;
						ChannelNode parent = (tn.Parent != null) ? tn.Parent.Tag as ChannelNode : null;
						VixenSystem.Nodes.RemoveNode(cn, parent, true);
						if (_displayedNode == cn) {
							_displayedNode = null;
						}
					}
				}
			}

			PopulateNodeTree();
		}

		private void buttonCreateGroup_Click(object sender, EventArgs e)
		{
			if (multiSelectTreeviewChannelsGroups.SelectedNodes.Count > 0)
			{
				String groupName;
				using (CommonElements.TextDialog textDialog = new CommonElements.TextDialog("Group Name?")) {
					if (textDialog.ShowDialog() == DialogResult.OK) {
						if (textDialog.Response == "")
							groupName = "Unnamed Group";
						else
							groupName = textDialog.Response;

						ChannelNode newNode = VixenSystem.Nodes.AddNewNode(groupName);

						foreach (TreeNode tn in multiSelectTreeviewChannelsGroups.SelectedNodes) {
							newNode.AddChild(tn.Tag as ChannelNode);
						}

						PopulateNodeTree();
					}
				}
			}
		}

		private void buttonBulkRename_Click(object sender, EventArgs e)
		{
            if (multiSelectTreeviewChannelsGroups.SelectedNodes.Count > 0)
            {
                BulkChannelRename Formx = new BulkChannelRename();
                Formx.ShowDialog();
            }
		}

		private void buttonRenameItem_Click(object sender, EventArgs e)
		{
			if (textBoxName.Text.Trim() != "") {
				VixenSystem.Nodes.RenameNode(_displayedNode, textBoxName.Text.Trim());
			}
			PopulateNodeTree();
		}

		private void buttonRemovePatch_Click(object sender, EventArgs e)
		{
			if (listViewPatches.SelectedItems.Count > 0) {
				string message, title;
				if (listViewPatches.SelectedItems.Count == 1) {
					message = "Are you sure you want to remove the selected patch?";
					title = "Remove Patch?";
				} else {
					message = "Are you sure you want to remove the selected patches?";
					title = "Remove Patches?";
				}
				if (MessageBox.Show(message, title, MessageBoxButtons.OKCancel) == DialogResult.OK) {
					foreach (ListViewItem item in listViewPatches.SelectedItems) {
						if (item.Tag is ControllerReference) {
							_displayedNode.Channel.Patch.Remove(item.Tag as ControllerReference);
						} else {
							VixenSystem.Logging.Error("ConfigChannels: Trying to remove patch, but it's not a ControllerReference");
						}
					}
				}
			}
		}

		private void buttonAddPatch_Click(object sender, EventArgs e)
		{
			if (comboBoxPatchControllerSelect.SelectedIndex < 0 || numericUpDownPatchOutputSelect.Value <= 0)
				return;

			OutputController controller = VixenSystem.Controllers.Get((Guid)comboBoxPatchControllerSelect.SelectedValue);
			if (controller == null || controller.OutputCount < numericUpDownPatchOutputSelect.Value)
				return;

			if (_displayedNode.Channel == null) {
				_displayedNode.Channel = VixenSystem.Channels.AddChannel(_displayedNode.Name);
			}
			_displayedNode.Channel.Patch.Add(new ControllerReference((Guid)comboBoxPatchControllerSelect.SelectedValue, ((int)numericUpDownPatchOutputSelect.Value) - 1));

			PopulateCurrentPatchesArea(_displayedNode);
		}

		private void buttonAddProperty_Click(object sender, EventArgs e)
		{
			List<KeyValuePair<string, object>> properties = new List<KeyValuePair<string, object>>();
			foreach (KeyValuePair<Guid, string> kvp in ApplicationServices.GetAvailableModules<IPropertyModuleInstance>()) {
				properties.Add(new KeyValuePair<string, object>(kvp.Value, kvp.Key));
			}
			CommonElements.ListSelectDialog addForm = new CommonElements.ListSelectDialog("Add Property", (properties));
			if (addForm.ShowDialog() == DialogResult.OK) {
				_displayedNode.Properties.Add((Guid)addForm.selectedItem);
				PopulatePropertiesArea(_displayedNode);
			}
		}

		private void buttonConfigureProperty_Click(object sender, EventArgs e)
		{
			ConfigureSelectedProperty();
		}

		private void buttonRemoveProperty_Click(object sender, EventArgs e)
		{
			if (listViewProperties.SelectedItems.Count > 0) {
				string message, title;
				if (listViewProperties.SelectedItems.Count == 1) {
					message = "Are you sure you want to remove the selected property from the group?";
					title = "Remove Property?";
				} else {
					message = "Are you sure you want to remove the selected properties from the group?";
					title = "Remove Properties?";
				}
				if (MessageBox.Show(message, title, MessageBoxButtons.OKCancel) == DialogResult.OK) {
					foreach (ListViewItem item in listViewProperties.SelectedItems) {
						_displayedNode.Properties.Remove((item.Tag as IPropertyModuleInstance).Descriptor.TypeId);
					}

					PopulatePropertiesArea(_displayedNode);
				}
			}
		}

		#endregion


		#region Events

		private void multiSelectTreeviewChannelsGroups_AfterSelect(object sender, TreeViewEventArgs e)
		{
			PopulateFormWithNode(multiSelectTreeviewChannelsGroups.SelectedNode.Tag as ChannelNode, false);

			buttonDeleteNode.Enabled = (multiSelectTreeviewChannelsGroups.SelectedNodes.Count > 0);
			buttonCreateGroup.Enabled = (multiSelectTreeviewChannelsGroups.SelectedNodes.Count > 0);
			buttonBulkRename.Enabled = (multiSelectTreeviewChannelsGroups.SelectedNodes.Count > 0);
		}

		private void listViewProperties_MouseDoubleClick(object sender, MouseEventArgs e)
		{
			ConfigureSelectedProperty();
		}

		private void listViewProperties_SelectedIndexChanged(object sender, EventArgs e)
		{
			buttonConfigureProperty.Enabled = (listViewProperties.SelectedItems.Count == 1);
			buttonRemoveProperty.Enabled = (listViewProperties.SelectedItems.Count > 0);
		}

		private void comboBoxPatchControllerSelect_SelectedIndexChanged(object sender, EventArgs e)
		{
			PopulateAddPatchArea(_displayedNode);
		}

		private void listViewPatches_SelectedIndexChanged(object sender, EventArgs e)
		{
			buttonRemovePatch.Enabled = (listViewPatches.SelectedItems.Count > 0);
		}

		private void checkBoxDisableOutputs_CheckedChanged(object sender, EventArgs e)
		{
			if (_displayedNode != null) {
				if (_displayedNode.Channel.Masked != checkBoxDisableOutputs.Checked) {
					_displayedNode.Channel.Masked = checkBoxDisableOutputs.Checked;
					PopulateNodeTree();
					PopulateFormWithNode(_displayedNode, true);
				}
			}
		}

		#endregion


		#region Drag 'n' Drop Handlers

		private void multiSelectTreeviewChannelsGroupsDragFinishingHandler(object sender, DragFinishingEventArgs e)
		{
			// we want to finish off the drag ourselves, and not have the treeview control move the nodes around.
			// (In fact, in a lame attempt at 'data binding', we're going to completely redraw the tree after
			// making all the required changes from this drag-drop.) So set the flag to indicate that.
			e.FinishDrag = false;

			// first determine the node that they will be moved to. This will depend on if we are dragging onto a node
			// directly, or above/below one to reorder.
			ChannelNode newParentNode = null;						// the channelNode that the selected items will move to
			TreeNode expandNode = null;								// if we need to expand a node once we've moved everything
			int index = -1;

			if (e.DragBetweenNodes == DragBetweenNodes.DragOnTargetNode) {
				newParentNode = e.TargetNode.Tag as ChannelNode;
				expandNode = e.TargetNode;
			} else if (e.DragBetweenNodes == DragBetweenNodes.DragBelowTargetNode && e.TargetNode.IsExpanded) {
				newParentNode = e.TargetNode.Tag as ChannelNode;
				expandNode = e.TargetNode;
				index = 0;
			} else {
				if (e.TargetNode.Parent == null) {
					newParentNode = null;	// needs to go at the root level
				} else {
					newParentNode = e.TargetNode.Parent.Tag as ChannelNode;
				}

				if (e.DragBetweenNodes == DragBetweenNodes.DragAboveTargetNode) {
					index = e.TargetNode.Index;
				} else {
					index = e.TargetNode.Index + 1;
				}
			}

			// Check to see if the new parent node would be 'losing' the Channel (ie. becoming a
			// group instead of a leaf node with a channel/patches). Prompt the user first.
			if (newParentNode != null && newParentNode.Channel != null && newParentNode.Channel.Patch.Count() > 0) {
				string message = "Moving nodes into this Channel will convert it into a Group, which will remove any " +
					"patches it may have to outputs. Are you sure you want to continue?";
				string title = "Convert Channel to Group?";
				DialogResult result = MessageBox.Show(message, title, MessageBoxButtons.YesNoCancel);
				if (result != System.Windows.Forms.DialogResult.Yes) {
					return;
				}
			}

			// If moving channel nodes, we need to iterate through all selected treenodes, and remove them from
			// the parent in which they are selected (which we can determine from the treenode parent), and add them
			// to the target node. If copying, we need to just add them to the new parent node.
			foreach (TreeNode treeNode in e.SourceNodes) {
				ChannelNode sourceNode = treeNode.Tag as ChannelNode;
				ChannelNode oldParentNode = (treeNode.Parent != null) ? treeNode.Parent.Tag as ChannelNode : null;
				if (e.DragMode == DragDropEffects.Move) {
					if (index >= 0) {
						VixenSystem.Nodes.MoveNode(sourceNode, newParentNode, oldParentNode, index);
					} else {
						VixenSystem.Nodes.MoveNode(sourceNode, newParentNode, oldParentNode);
					}
				} else if (e.DragMode == DragDropEffects.Copy) {
					if (index >= 0) {
						// increment the index after every move, so the items are inserted in the correct order (if not, they would be reversed)
						VixenSystem.Nodes.AddChildToParent(sourceNode, newParentNode, index++);
					} else {
						VixenSystem.Nodes.AddChildToParent(sourceNode, newParentNode);
					}
				} else {
					VixenSystem.Logging.Warn("ConfigChannels: Trying to deal with a drag that is an unknown type!");
				}
			}

			if (expandNode != null)
				expandNode.Expand();
			
			PopulateNodeTree();
			PopulateFormWithNode(multiSelectTreeviewChannelsGroups.SelectedNode.Tag as ChannelNode, true);
		}

		private void multiSelectTreeviewChannelsGroupsDragVerifyHandler(object sender, DragVerifyEventArgs e)
		{
			// we need to go through all nodes that would be moved (source nodes), and check if there's any
			// problem moving any of them to the target node (circular references, etc.), since it's possible
			// for a channel to exist multiple times in the treeview as different treenodes.

			List<ChannelNode> nodes = new List<ChannelNode>(e.SourceNodes.Select(x => x.Tag as ChannelNode));

			// now get a list of invalid children for this target node, and check all the remaining nodes against it.
			// If any of them fail, the entire operation should fail, as it would be an invalid move.

			// the target node will be the actual target node if it is directly on it, otherwise it would be the parent
			// of the target node if it would be dragged above/below the taget element (as it would be put alongside it).
			// also keep track of the 'permitted' nodes: this is used when dragging alongside another element: any children
			// of the new parent node are considered OK, as we might just be shuffling them around. Normally, this would
			// be A Bad Thing, since it would seem like we're adding a child to the group it's already in. (This is only
			// the case when moving; if copying, it should be disabled. That's checked later.)
			IEnumerable<ChannelNode> invalidNodesForTarget = null;
			IEnumerable<ChannelNode> permittedNodesForTarget = null;

			if (e.DragBetweenNodes == DragBetweenNodes.DragOnTargetNode || e.DragBetweenNodes == DragBetweenNodes.DragBelowTargetNode && e.TargetNode.IsExpanded) {
				invalidNodesForTarget = (e.TargetNode.Tag as ChannelNode).InvalidChildren();
				permittedNodesForTarget = new HashSet<ChannelNode>();
			} else {
				if (e.TargetNode.Parent == null) {
					invalidNodesForTarget = VixenSystem.Nodes.InvalidRootNodes;
					permittedNodesForTarget = VixenSystem.Nodes.RootNodes;
				} else {
					invalidNodesForTarget = (e.TargetNode.Parent.Tag as ChannelNode).InvalidChildren();
					permittedNodesForTarget = (e.TargetNode.Parent.Tag as ChannelNode).Children;
				}
			}

			if ((e.KeyState & 8) != 0) {		// the CTRL key
				e.DragMode = DragDropEffects.Copy;
				permittedNodesForTarget = new HashSet<ChannelNode>();
			} else {
				e.DragMode = DragDropEffects.Move;
			}

			IEnumerable<ChannelNode> invalidSourceNodes = invalidNodesForTarget.Intersect(nodes);
			if (invalidSourceNodes.Count() > 0) {
				if (invalidSourceNodes.Intersect(permittedNodesForTarget).Count() == invalidSourceNodes.Count())
					e.ValidDragTarget = true;
				else
					e.ValidDragTarget = false;
			} else {
				e.ValidDragTarget = true;
			}
		}
		
		#endregion


		#region Helper functions
		private void ConfigureSelectedProperty()
		{
			if (listViewProperties.SelectedItems.Count == 1) {
				(listViewProperties.SelectedItems[0].Tag as IPropertyModuleInstance).Setup();
			}
		}

		private int GetNodeParentGroupCount(ChannelNode node)
		{
			int count = node.Parents.Count();
			if(VixenSystem.Nodes.RootNodes.Contains(node))
				count--;
			return count;
		}

		private List<string> GetNodeParentGroupNames(ChannelNode node, int maxNames = Int32.MaxValue)
		{
			List<string> result = new List<string>();
			foreach (ChannelNode parent in node.Parents) {
				if (maxNames <= 0) {
					break;
				}
				if (parent.Name != "Root") {
					result.Add(parent.Name);
				}
				maxNames--;
			}

			return result;
		}



		#endregion

		public class ComboBoxControllerItem
		{
			public string Name { get; set; }
			public Guid Id { get; set; }
		}

	}
}
