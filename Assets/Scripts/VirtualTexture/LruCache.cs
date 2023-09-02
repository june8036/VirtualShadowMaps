namespace VirtualTexture
{
    public sealed class LruCache
    {
        public class NodeInfo
        {
            public int id = 0;
            public NodeInfo next { get; set; }
            public NodeInfo prev { get; set; }
        }

        private NodeInfo [] m_AllNodes;
        private NodeInfo m_Head = null;
        private NodeInfo m_Tail = null;

        public int first { get { return m_Head.id; } }

        public LruCache(int count)
        {
            m_AllNodes = new NodeInfo[count];

            for (int i= 0;i < count;i++)
            {
                m_AllNodes[i] = new NodeInfo()
                {
                    id = i,
                };
            }

            for (int i = 0; i < count; i++)
            {
                m_AllNodes[i].next = (i + 1 < count) ? m_AllNodes[i + 1] : null;
                m_AllNodes[i].prev = (i != 0) ? m_AllNodes[i - 1] : null;
            }

            m_Head = m_AllNodes[0];
            m_Tail = m_AllNodes[count - 1];
        }

        public void Clear()
		{
            if (m_AllNodes.Length > 0)
			{
                m_Head = m_AllNodes[0];
                m_Tail = m_AllNodes[m_AllNodes.Length - 1];
            }
        }

        public bool SetActive(int id)
        {
            if (id < 0 || id >= m_AllNodes.Length)
                return false;

            var node = m_AllNodes[id];
            if (node == m_Tail)
            {
                return true;
            }

            Remove(node);
            AddLast(node);
            return true;
        }

        private void AddLast(NodeInfo node)
        {
            var lastTail = m_Tail;
            lastTail.next = node;
            m_Tail = node;
            node.prev = lastTail;
        }

        private void Remove(NodeInfo node)
        {
            if (m_Head == node)
            {
                m_Head = node.next;
            }
            else
            {
                node.prev.next = node.next;
                node.next.prev = node.prev;
            }
        }
    }
}